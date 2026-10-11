using DvCms.Application.Accessi;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Catalogo del negozio (vedi <see cref="IServizioCatalogo"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed class ServizioCatalogo : IServizioCatalogo
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioCatalogo(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public Task<bool> CommercioAttivoAsync(CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return _contesto.Siti.AsNoTracking().AnyAsync(sito => sito.Id == idSito && sito.CommercioAttivo, annullamento);
    }

    public async Task<IReadOnlyList<LinguaCatalogo>> LingueAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        return [.. (await LingueDelSito.ElencaAsync(_contesto, _tenant.IdSito, annullamento)).Select(l => new LinguaCatalogo(l.Codice, l.Nome, l.Predefinita))];
    }

    public async Task<IReadOnlyList<RiepilogoProdotto>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        if (!await CommercioAttivoAsync(annullamento))
        {
            return [];
        }

        var idSito = _tenant.IdSito;
        var predefinita = (await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento)).FirstOrDefault(l => l.Predefinita);
        var prodotti = await _contesto.Prodotti.AsNoTracking().Where(p => p.IdSito == idSito).OrderByDescending(p => p.AggiornatoIlUtc).ToListAsync(annullamento);
        var nomi = predefinita is null
            ? []
            : await _contesto.TraduzioniProdotto.AsNoTracking().Where(t => t.IdSito == idSito && t.IdLingua == predefinita.IdLingua).ToDictionaryAsync(t => t.IdProdotto, t => t.Nome, annullamento);
        var varianti = await _contesto.VariantiProdotto.AsNoTracking().Where(v => v.IdSito == idSito).ToListAsync(annullamento);
        var perProdotto = varianti.GroupBy(v => v.IdProdotto).ToDictionary(g => g.Key, g => g.ToList());

        return [.. prodotti.Select(p =>
        {
            var proprie = perProdotto.GetValueOrDefault(p.Id) ?? [];
            var attive = proprie.Where(v => v.Attiva).ToList();
            return new RiepilogoProdotto(
                p.Id,
                nomi.GetValueOrDefault(p.Id) ?? string.Empty,
                p.Stato,
                proprie.Count,
                attive.Count > 0 ? attive.Min(v => v.PrezzoLordo) : null,
                VarianteProdotto.ValutaPredefinita,
                p.AggiornatoIlUtc);
        })];
    }

    public async Task<DettaglioProdotto?> LeggiAsync(Guid idAttore, Guid idProdotto, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        if (!await CommercioAttivoAsync(annullamento))
        {
            return null;
        }

        var idSito = _tenant.IdSito;
        var prodotto = await _contesto.Prodotti.AsNoTracking().SingleOrDefaultAsync(p => p.Id == idProdotto && p.IdSito == idSito, annullamento);
        if (prodotto is null)
        {
            return null;
        }

        var lingue = await _contesto.Lingue.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Codice, annullamento);
        var traduzioni = await _contesto.TraduzioniProdotto.AsNoTracking().Where(t => t.IdProdotto == idProdotto).ToListAsync(annullamento);
        var varianti = await _contesto.VariantiProdotto.AsNoTracking().Where(v => v.IdProdotto == idProdotto).OrderBy(v => v.Posizione).ToListAsync(annullamento);
        var idVarianti = varianti.Select(v => v.Id).ToList();
        var etichette = await _contesto.TraduzioniVarianteProdotto.AsNoTracking().Where(t => idVarianti.Contains(t.IdVariante)).ToListAsync(annullamento);

        return new DettaglioProdotto(
            prodotto.Id,
            prodotto.Stato,
            prodotto.AliquotaIva,
            Convert.ToBase64String(prodotto.TokenConcorrenza),
            [.. traduzioni.Select(t => new TraduzioneProdottoDati(lingue.GetValueOrDefault(t.IdLingua) ?? string.Empty, t.Nome, t.Descrizione))],
            [.. varianti.Select(v => new VarianteDettaglio(
                v.Id,
                v.Sku,
                v.PrezzoLordo,
                v.Valuta,
                v.Attiva,
                etichette.Where(e => e.IdVariante == v.Id).ToDictionary(e => lingue.GetValueOrDefault(e.IdLingua) ?? string.Empty, e => e.Etichetta)))]);
    }

    public async Task<EsitoOperazione> SalvaAsync(Guid idAttore, DatiProdotto dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "SalvataggioProdotto");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!await CommercioAttivoAsync(annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        var lingueSito = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var predefinita = lingueSito.FirstOrDefault(l => l.Predefinita);
        var perCodice = lingueSito.ToDictionary(l => l.Codice, StringComparer.OrdinalIgnoreCase);

        var validazione = Valida(dati, predefinita?.Codice, perCodice);
        if (validazione.Errori.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, validazione.Errori);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var nuovo = dati.Id is null;
        Prodotto? esistente = null;
        if (!nuovo)
        {
            esistente = await _contesto.Prodotti.SingleOrDefaultAsync(p => p.Id == dati.Id && p.IdSito == idSito, annullamento);
            if (esistente is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            if (!TentaLeggereToken(dati.TokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["TokenConcorrenza"]);
            }

            _contesto.Entry(esistente).Property(p => p.TokenConcorrenza).OriginalValue = token;
        }

        var prodotto = esistente ?? new Prodotto { Id = Identificativi.Nuovo(_orologio), IdTenant = idTenant, IdSito = idSito, CreatoIlUtc = adesso };
        if (nuovo)
        {
            _contesto.Prodotti.Add(prodotto);
        }

        prodotto.Stato = dati.Stato;
        prodotto.AliquotaIva = dati.AliquotaIva;
        prodotto.AggiornatoIlUtc = adesso;

        await SincronizzaTraduzioniAsync(prodotto, dati, perCodice, annullamento);
        var esitoVarianti = await SincronizzaVariantiAsync(prodotto, dati, perCodice, nuovo, annullamento);
        if (esitoVarianti is not null)
        {
            _contesto.ChangeTracker.Clear();
            return esitoVarianti;
        }

        try
        {
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
        catch (DbUpdateException)
        {
            // Race sul codice articolo (indice unico): un'altra richiesta l'ha preso fra il controllo e il salvataggio.
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.GiaPresente, null, ["Sku"]);
        }

        await _audit.RegistraAsync(
            nuovo ? AzioniAudit.CreazioneProdotto : AzioniAudit.ModificaProdotto,
            $"Prodotto:{prodotto.Id}",
            RegistroAudit.EsitoRiuscito,
            idTenant,
            attore.Id,
            $"stato {prodotto.Stato}, {dati.Varianti.Count} varianti",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, prodotto.Id);
    }

    private async Task SincronizzaTraduzioniAsync(Prodotto prodotto, DatiProdotto dati, Dictionary<string, LinguaAbilitata> perCodice, CancellationToken annullamento)
    {
        var esistenti = await _contesto.TraduzioniProdotto.Where(t => t.IdProdotto == prodotto.Id).ToListAsync(annullamento);
        var volute = dati.Traduzioni.ToDictionary(t => perCodice[t.CodiceLingua.Trim()].IdLingua);
        foreach (var vecchia in esistenti.Where(t => !volute.ContainsKey(t.IdLingua)))
        {
            _contesto.TraduzioniProdotto.Remove(vecchia);
        }

        foreach (var (idLingua, voluta) in volute)
        {
            var testo = voluta.Descrizione?.Trim() ?? string.Empty;
            var attuale = esistenti.SingleOrDefault(t => t.IdLingua == idLingua);
            if (attuale is null)
            {
                _contesto.TraduzioniProdotto.Add(new TraduzioneProdotto { IdTenant = prodotto.IdTenant, IdSito = prodotto.IdSito, IdProdotto = prodotto.Id, IdLingua = idLingua, Nome = voluta.Nome.Trim(), Descrizione = testo });
            }
            else
            {
                attuale.Nome = voluta.Nome.Trim();
                attuale.Descrizione = testo;
            }
        }
    }

    /// <summary>Allinea le varianti allo stato richiesto. Restituisce un esito d'errore, o nullo se va tutto bene.</summary>
    private async Task<EsitoOperazione?> SincronizzaVariantiAsync(Prodotto prodotto, DatiProdotto dati, Dictionary<string, LinguaAbilitata> perCodice, bool nuovo, CancellationToken annullamento)
    {
        var esistenti = nuovo ? [] : await _contesto.VariantiProdotto.Where(v => v.IdProdotto == prodotto.Id).ToListAsync(annullamento);
        var identificativiVoluti = dati.Varianti.Where(v => v.Id is not null).Select(v => v.Id!.Value).ToHashSet();
        if (identificativiVoluti.Any(id => esistenti.All(e => e.Id != id)))
        {
            // Una variante che non è di questo prodotto (o di questo sito): mai accettata.
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Varianti"]);
        }

        // Codici già usati nel sito da varianti di altri prodotti o non toccate da questo salvataggio.
        var codici = dati.Varianti.Select(v => { VarianteProdotto.TentaNormalizzareSku(v.Sku, out var sku); return sku; }).ToList();
        var usati = await _contesto.VariantiProdotto.AsNoTracking()
            .Where(v => v.IdSito == prodotto.IdSito && codici.Contains(v.Sku) && v.IdProdotto != prodotto.Id)
            .Select(v => v.Sku)
            .ToListAsync(annullamento);
        if (usati.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.GiaPresente, null, ["Sku"]);
        }

        var vecchie = esistenti.Where(e => !identificativiVoluti.Contains(e.Id)).ToList();
        if (vecchie.Count > 0)
        {
            var idVecchie = vecchie.Select(v => v.Id).ToList();
            _contesto.TraduzioniVarianteProdotto.RemoveRange(await _contesto.TraduzioniVarianteProdotto.Where(t => idVecchie.Contains(t.IdVariante)).ToListAsync(annullamento));
            // Le righe dei carrelli (temporanee) che puntano a una variante tolta si tolgono con lei; gli ordini, quando ci saranno,
            // conservano i loro snapshot e una variante già ordinata si disattiva soltanto.
            _contesto.RigheCarrello.RemoveRange(await _contesto.RigheCarrello.Where(r => idVecchie.Contains(r.IdVariante)).ToListAsync(annullamento));
            _contesto.VariantiProdotto.RemoveRange(vecchie);
        }

        // Prima si liberano i codici delle varianti che cambiano codice fra loro: l'indice unico si controlla alla fine del salvataggio,
        // non riga per riga, quindi due varianti possono scambiarsi il codice solo in due salvataggi (caso raro, accettato).
        var posizione = 0;
        foreach (var voluta in dati.Varianti)
        {
            VarianteProdotto.TentaNormalizzareSku(voluta.Sku, out var sku);
            var variante = voluta.Id is { } id ? esistenti.Single(e => e.Id == id) : null;
            if (variante is null)
            {
                variante = new VarianteProdotto { Id = Identificativi.Nuovo(_orologio), IdTenant = prodotto.IdTenant, IdSito = prodotto.IdSito, IdProdotto = prodotto.Id };
                _contesto.VariantiProdotto.Add(variante);
            }

            variante.Sku = sku;
            variante.PrezzoLordo = voluta.PrezzoLordo;
            variante.Valuta = VarianteProdotto.ValutaPredefinita;
            variante.Attiva = voluta.Attiva;
            variante.Posizione = posizione++;

            var attuali = variante.Id == Guid.Empty || voluta.Id is null
                ? []
                : await _contesto.TraduzioniVarianteProdotto.Where(t => t.IdVariante == variante.Id).ToListAsync(annullamento);
            var volute = (voluta.Etichette ?? new Dictionary<string, string>()).Where(e => !string.IsNullOrWhiteSpace(e.Value)).ToDictionary(e => perCodice[e.Key.Trim()].IdLingua, e => e.Value.Trim());
            foreach (var vecchia in attuali.Where(t => !volute.ContainsKey(t.IdLingua)))
            {
                _contesto.TraduzioniVarianteProdotto.Remove(vecchia);
            }

            foreach (var (idLingua, testo) in volute)
            {
                var corrente = attuali.SingleOrDefault(t => t.IdLingua == idLingua);
                if (corrente is null)
                {
                    _contesto.TraduzioniVarianteProdotto.Add(new TraduzioneVarianteProdotto { IdTenant = prodotto.IdTenant, IdSito = prodotto.IdSito, IdVariante = variante.Id, IdLingua = idLingua, Etichetta = testo });
                }
                else
                {
                    corrente.Etichetta = testo;
                }
            }
        }

        return null;
    }

    private sealed record EsitoValidazione(List<string> Errori);

    private static EsitoValidazione Valida(DatiProdotto dati, string? codicePredefinito, Dictionary<string, LinguaAbilitata> perCodice)
    {
        var errori = new List<string>();
        void Errore(string campo)
        {
            if (!errori.Contains(campo))
            {
                errori.Add(campo);
            }
        }

        if (!Enum.IsDefined(dati.Stato) || (dati.Id is null && dati.Stato == StatoProdotto.Archiviato))
        {
            Errore("Stato");
        }

        if (!AliquoteIva.EAmmessa(dati.AliquotaIva))
        {
            Errore("AliquotaIva");
        }

        // Traduzioni: solo lingue del sito, senza ripetizioni, nome obbligatorio e almeno nella lingua predefinita.
        var viste = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var traduzione in dati.Traduzioni ?? [])
        {
            var codice = traduzione.CodiceLingua?.Trim() ?? string.Empty;
            if (!perCodice.ContainsKey(codice) || !viste.Add(codice))
            {
                Errore("Traduzioni");
                continue;
            }

            if (!TestoValido(traduzione.Nome, Prodotto.LunghezzaMassimaNome, obbligatorio: true, ammettiAccapo: false))
            {
                Errore("Nome");
            }

            if (!TestoValido(traduzione.Descrizione, Prodotto.LunghezzaMassimaDescrizione, obbligatorio: false, ammettiAccapo: true))
            {
                Errore("Descrizione");
            }
        }

        if (codicePredefinito is null || !viste.Contains(codicePredefinito))
        {
            Errore("Nome");
        }

        // Varianti: da una a cento; codice valido e non ripetuto, prezzo valido, valuta ammessa, etichette nelle lingue del sito.
        var varianti = dati.Varianti ?? [];
        if (varianti.Count == 0 || varianti.Count > Prodotto.NumeroMassimoVarianti)
        {
            Errore("Varianti");
        }

        var codici = new HashSet<string>(StringComparer.Ordinal);
        var identificativi = new HashSet<Guid>();
        foreach (var variante in varianti)
        {
            if (!VarianteProdotto.TentaNormalizzareSku(variante.Sku, out var sku) || !codici.Add(sku))
            {
                Errore("Sku");
            }

            if (!VarianteProdotto.PrezzoValido(variante.PrezzoLordo))
            {
                Errore("Prezzo");
            }

            if (variante.Valuta is not null && !VarianteProdotto.ValutaAmmessa(variante.Valuta))
            {
                Errore("Valuta");
            }

            if (variante.Id is { } id && !identificativi.Add(id))
            {
                Errore("Varianti");
            }

            foreach (var (codice, etichetta) in variante.Etichette ?? new Dictionary<string, string>())
            {
                if (!perCodice.ContainsKey(codice?.Trim() ?? string.Empty)
                    || !TestoValido(etichetta, VarianteProdotto.LunghezzaMassimaEtichetta, obbligatorio: false, ammettiAccapo: false))
                {
                    Errore("Etichetta");
                }
            }

            // Con più varianti servono etichette che le distinguano, almeno nella lingua predefinita.
            if (varianti.Count > 1 && codicePredefinito is not null
                && !(variante.Etichette ?? new Dictionary<string, string>()).Any(e => string.Equals(e.Key?.Trim(), codicePredefinito, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Value)))
            {
                Errore("Etichetta");
            }
        }

        if (dati.Stato == StatoProdotto.Pubblicato && !varianti.Any(v => v.Attiva))
        {
            Errore("Varianti");
        }

        return new EsitoValidazione(errori);
    }

    private static bool TestoValido(string? testo, int lunghezzaMassima, bool obbligatorio, bool ammettiAccapo)
    {
        var pulito = testo?.Trim() ?? string.Empty;
        if (pulito.Length == 0)
        {
            return !obbligatorio;
        }

        return pulito.Length <= lunghezzaMassima
            && !pulito.Any(c => char.IsControl(c) && !(ammettiAccapo && c is '\n' or '\r' or '\t'));
    }

    private static bool TentaLeggereToken(string? testo, out byte[] token)
    {
        token = [];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return false;
        }

        try
        {
            token = Convert.FromBase64String(testo);
            return token.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task RichiediAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }
    }

    private Task<Utenza?> TrovaAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
    }

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "Prodotto",
            RegistroAudit.EsitoNegato,
            _tenant.IdTenant,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {azione}",
            CancellationToken.None);
    }
}

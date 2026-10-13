using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Negozio;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Carrello dell'acquirente (vedi <see cref="IServizioCarrello"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed class ServizioCarrello : IServizioCarrello
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly TimeProvider _orologio;

    public ServizioCarrello(ContestoDatabase contesto, IContestoTenant tenant, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _orologio = orologio;
    }

    public async Task<CarrelloVista> LeggiAsync(Guid idAccount, string codiceLingua, CancellationToken annullamento)
    {
        await RichiediAccountAsync(idAccount, annullamento);
        var vuoto = new CarrelloVista([], [], 0m, 0m, 0m, VarianteProdotto.ValutaPredefinita);
        if (!await CommercioAttivoAsync(annullamento))
        {
            return vuoto;
        }

        var idSito = _tenant.IdSito;
        var carrello = await _contesto.Carrelli.AsNoTracking().SingleOrDefaultAsync(c => c.IdSito == idSito && c.IdUtenza == idAccount, annullamento);
        if (carrello is null)
        {
            return vuoto;
        }

        var righe = await _contesto.RigheCarrello.AsNoTracking().Where(r => r.IdCarrello == carrello.Id).OrderBy(r => r.AggiuntaIlUtc).ToListAsync(annullamento);
        if (righe.Count == 0)
        {
            return vuoto;
        }

        var idVarianti = righe.Select(r => r.IdVariante).ToList();
        var dati = await (
            from v in _contesto.VariantiProdotto.AsNoTracking()
            join p in _contesto.Prodotti.AsNoTracking() on v.IdProdotto equals p.Id
            where idVarianti.Contains(v.Id) && v.IdSito == idSito
            select new { Variante = v, Prodotto = p }).ToListAsync(annullamento);

        var lingue = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var idPredefinita = lingue.FirstOrDefault(l => l.Predefinita)?.IdLingua;
        var idRichiesta = lingue.FirstOrDefault(l => string.Equals(l.Codice, codiceLingua, StringComparison.OrdinalIgnoreCase))?.IdLingua ?? idPredefinita;
        var idProdotti = dati.Select(d => d.Prodotto.Id).Distinct().ToList();
        var nomi = await _contesto.TraduzioniProdotto.AsNoTracking().Where(t => idProdotti.Contains(t.IdProdotto)).ToListAsync(annullamento);
        var etichette = await _contesto.TraduzioniVarianteProdotto.AsNoTracking().Where(t => idVarianti.Contains(t.IdVariante)).ToListAsync(annullamento);

        var giacenze = await _contesto.GiacenzeVarianti.AsNoTracking().Where(g => g.IdSito == idSito && idVarianti.Contains(g.IdVariante)).ToDictionaryAsync(g => g.IdVariante, annullamento);
        var viste = new List<RigaCarrelloVista>();
        var calcolate = new List<RigaCalcolata>();
        var nonDisponibili = new List<Guid>();
        foreach (var riga in righe)
        {
            var d = dati.SingleOrDefault(x => x.Variante.Id == riga.IdVariante);
            if (d is null || !d.Variante.Attiva || d.Prodotto.Stato != StatoProdotto.Pubblicato)
            {
                nonDisponibili.Add(riga.IdVariante);
                continue;
            }

            var calcolata = CalcolatoreTotali.CalcolaRiga(d.Variante.PrezzoLordo, riga.Quantita, d.Prodotto.AliquotaIva);
            calcolate.Add(calcolata);
            viste.Add(new RigaCarrelloVista(
                d.Variante.Id,
                d.Variante.Sku,
                Testo(nomi.Where(t => t.IdProdotto == d.Prodotto.Id), t => t.IdLingua, t => t.Nome, idRichiesta, idPredefinita),
                TestoOpzionale(etichette.Where(t => t.IdVariante == d.Variante.Id), t => t.IdLingua, t => t.Etichetta, idRichiesta, idPredefinita),
                riga.Quantita,
                calcolata.PrezzoUnitarioLordo,
                calcolata.Aliquota,
                calcolata.Lordo,
                calcolata.Imponibile,
                calcolata.Iva,
                giacenze.TryGetValue(d.Variante.Id, out var giacenza) ? Math.Clamp(giacenza.Disponibile - giacenza.Riservata, 0, RigaCarrello.QuantitaMassima) : RigaCarrello.QuantitaMassima));
        }

        var totali = CalcolatoreTotali.Somma(calcolate);
        return new CarrelloVista(viste, nonDisponibili, totali.Lordo, totali.Imponibile, totali.Iva, VarianteProdotto.ValutaPredefinita);
    }

    public async Task<EsitoOperazione> AggiungiAsync(Guid idAccount, Guid idVariante, int quantita, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!await CommercioAttivoAsync(annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        if (quantita < 1 || quantita > RigaCarrello.QuantitaMassima)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Quantita"]);
        }

        if (!await VarianteAcquistabileAsync(idVariante, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var carrello = await OttieniOCreaAsync(idAccount, annullamento);
        var righe = await _contesto.RigheCarrello.Where(r => r.IdCarrello == carrello.Id).ToListAsync(annullamento);
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var esistente = righe.SingleOrDefault(r => r.IdVariante == idVariante);
        if (esistente is null)
        {
            if (righe.Count >= Carrello.RigheMassime)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Righe"]);
            }

            _contesto.RigheCarrello.Add(new RigaCarrello { IdTenant = carrello.IdTenant, IdSito = carrello.IdSito, IdCarrello = carrello.Id, IdVariante = idVariante, Quantita = quantita, AggiuntaIlUtc = adesso });
        }
        else
        {
            if (esistente.Quantita + quantita > RigaCarrello.QuantitaMassima)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Quantita"]);
            }

            esistente.Quantita += quantita;
        }

        carrello.AggiornatoIlUtc = adesso;
        return await SalvaAsync(carrello.Id, annullamento);
    }

    public async Task<EsitoOperazione> ImpostaQuantitaAsync(Guid idAccount, Guid idVariante, int quantita, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!await CommercioAttivoAsync(annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        if (quantita < 0 || quantita > RigaCarrello.QuantitaMassima)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Quantita"]);
        }

        var idSito = _tenant.IdSito;
        var carrello = await _contesto.Carrelli.SingleOrDefaultAsync(c => c.IdSito == idSito && c.IdUtenza == idAccount, annullamento);
        var riga = carrello is null ? null : await _contesto.RigheCarrello.SingleOrDefaultAsync(r => r.IdCarrello == carrello.Id && r.IdVariante == idVariante, annullamento);
        if (carrello is null || riga is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (quantita == 0)
        {
            _contesto.RigheCarrello.Remove(riga);
        }
        else
        {
            // Aumentare o mantenere una riga richiede che si possa ancora acquistare; toglierla è sempre possibile.
            if (!await VarianteAcquistabileAsync(idVariante, annullamento))
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            riga.Quantita = quantita;
        }

        carrello.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        return await SalvaAsync(carrello.Id, annullamento);
    }

    public async Task<EsitoOperazione> SvuotaAsync(Guid idAccount, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        var carrello = await _contesto.Carrelli.SingleOrDefaultAsync(c => c.IdSito == idSito && c.IdUtenza == idAccount, annullamento);
        if (carrello is null)
        {
            return new EsitoOperazione(CodiceEsito.Riuscito);
        }

        _contesto.RigheCarrello.RemoveRange(await _contesto.RigheCarrello.Where(r => r.IdCarrello == carrello.Id).ToListAsync(annullamento));
        carrello.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        return await SalvaAsync(carrello.Id, annullamento);
    }

    private async Task<EsitoOperazione> SalvaAsync(Guid idCarrello, CancellationToken annullamento)
    {
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, idCarrello);
        }
        catch (DbUpdateException)
        {
            // Due richieste insieme sulla stessa riga o sullo stesso carrello: la seconda riprova dall'interfaccia.
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
    }

    private async Task<Carrello> OttieniOCreaAsync(Guid idAccount, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var esistente = await _contesto.Carrelli.SingleOrDefaultAsync(c => c.IdSito == idSito && c.IdUtenza == idAccount, annullamento);
        if (esistente is not null)
        {
            return esistente;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var nuovo = new Carrello { Id = Identificativi.Nuovo(_orologio), IdTenant = _tenant.IdTenant, IdSito = idSito, IdUtenza = idAccount, CreatoIlUtc = adesso, AggiornatoIlUtc = adesso };
        _contesto.Carrelli.Add(nuovo);
        return nuovo;
    }

    private Task<bool> VarianteAcquistabileAsync(Guid idVariante, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return (
            from v in _contesto.VariantiProdotto.AsNoTracking()
            join p in _contesto.Prodotti.AsNoTracking() on v.IdProdotto equals p.Id
            where v.Id == idVariante && v.IdSito == idSito && v.Attiva && p.Stato == StatoProdotto.Pubblicato
            select v.Id).AnyAsync(annullamento);
    }

    private Task<bool> CommercioAttivoAsync(CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return _contesto.Siti.AsNoTracking().AnyAsync(sito => sito.Id == idSito && sito.CommercioAttivo, annullamento);
    }

    private async Task RichiediAccountAsync(Guid idAccount, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli account autenticati del tenant.");
        }
    }

    /// <summary>Può ordinare chi ha un account attivo: <c>Utente</c> o <c>TenantAdmin</c> del tenant dell'host, <c>PlatformAdmin</c> ovunque (ADR-0011).</summary>
    private Task<Utenza?> TrovaAccountAsync(Guid idAccount, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAccount
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.IdTenant == idTenant && (candidata.Ruolo == RuoloUtenza.Utente || candidata.Ruolo == RuoloUtenza.TenantAdmin))),
            annullamento);
    }

    private static string Testo<T>(IEnumerable<T> traduzioni, Func<T, Guid> lingua, Func<T, string> testo, Guid? idRichiesta, Guid? idPredefinita) =>
        TestoOpzionale(traduzioni, lingua, testo, idRichiesta, idPredefinita) ?? string.Empty;

    private static string? TestoOpzionale<T>(IEnumerable<T> traduzioni, Func<T, Guid> lingua, Func<T, string> testo, Guid? idRichiesta, Guid? idPredefinita)
    {
        var elenco = traduzioni.ToList();
        var scelta = elenco.FirstOrDefault(t => idRichiesta is { } r && lingua(t) == r)
            ?? elenco.FirstOrDefault(t => idPredefinita is { } p && lingua(t) == p);
        return scelta is null ? null : testo(scelta);
    }
}

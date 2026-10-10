using DvCms.Application.Accessi;
using DvCms.Application.Cms;
using DvCms.Application.Outbox;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Cms;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Cms;

public sealed class ServizioPagine : IServizioPagine
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly IOutbox _outbox;
    private readonly RegistroModelliPagina _modelli;
    private readonly IInvalidazioneCache _invalidazione;
    private readonly TimeProvider _orologio;

    public ServizioPagine(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IServizioAudit audit,
        IOutbox outbox,
        RegistroModelliPagina modelli,
        IInvalidazioneCache invalidazione,
        TimeProvider orologio)
    {
        _invalidazione = invalidazione;
        _outbox = outbox;
        _modelli = modelli;
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<LinguaDisponibile>> ElencaLingueAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        return [.. (await LingueDelSito.ElencaAsync(_contesto, _tenant.IdSito, annullamento)).Select(lingua => new LinguaDisponibile(lingua.Codice, lingua.Nome))];
    }

    public async Task<IReadOnlyList<RiepilogoPagina>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        var idSito = _tenant.IdSito;

        return await (
            from pagina in _contesto.Pagine.AsNoTracking()
            join lingua in _contesto.Lingue.AsNoTracking() on pagina.IdLingua equals lingua.Id
            join bozza in _contesto.RevisioniPagina.AsNoTracking() on pagina.IdUltimaRevisioneBozza equals bozza.Id
            where pagina.IdSito == idSito
            orderby pagina.Slug, lingua.Codice
            select new RiepilogoPagina(
                pagina.Id,
                pagina.Slug,
                bozza.Titolo,
                lingua.Codice,
                pagina.Stato,
                pagina.PaginaIniziale,
                pagina.IdRevisionePubblicata != pagina.IdUltimaRevisioneBozza,
                pagina.AggiornatoIlUtc))
            .ToListAsync(annullamento);
    }

    public async Task<DettaglioPagina?> LeggiAsync(Guid idAttore, Guid idPagina, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        var idSito = _tenant.IdSito;

        var pagina = await _contesto.Pagine.AsNoTracking().SingleOrDefaultAsync(candidata => candidata.Id == idPagina && candidata.IdSito == idSito, annullamento);
        if (pagina is null || pagina.IdUltimaRevisioneBozza is not { } idBozza)
        {
            return null;
        }

        var codiceLingua = await _contesto.Lingue.Where(lingua => lingua.Id == pagina.IdLingua).Select(lingua => lingua.Codice).SingleAsync(annullamento);
        var revisioni = await (
            from revisione in _contesto.RevisioniPagina.AsNoTracking()
            join utenza in _contesto.Utenze.IgnoreQueryFilters().AsNoTracking() on revisione.CreatoDaIdUtenza equals utenza.Id into autori
            from autore in autori.DefaultIfEmpty()
            where revisione.IdPagina == idPagina
            orderby revisione.NumeroRevisione descending
            select new RevisioneInElenco(
                revisione.Id,
                revisione.NumeroRevisione,
                revisione.Titolo,
                revisione.NotaRevisione,
                autore.Ruolo == RuoloUtenza.PlatformAdmin ? null : autore.Email,
                revisione.CreatoIlUtc,
                revisione.Id == pagina.IdRevisionePubblicata,
                revisione.Id == idBozza,
                autore.Ruolo == RuoloUtenza.PlatformAdmin))
            .ToListAsync(annullamento);
        var bozza = await _contesto.RevisioniPagina.AsNoTracking().SingleAsync(revisione => revisione.Id == idBozza, annullamento);
        var gruppo = await (
            from altra in _contesto.Pagine.AsNoTracking()
            join lingua in _contesto.Lingue.AsNoTracking() on altra.IdLingua equals lingua.Id
            where altra.IdSito == idSito && altra.IdGruppoTraduzione == pagina.IdGruppoTraduzione && altra.Id != pagina.Id
            orderby lingua.Codice
            select new TraduzionePagina(altra.Id, lingua.Codice, altra.Slug, altra.Stato))
            .ToListAsync(annullamento);
        var reindirizzamenti = await _contesto.RedirectPagine.AsNoTracking()
            .Where(redirect => redirect.IdPagina == idPagina)
            .OrderBy(redirect => redirect.SlugOrigine)
            .Select(redirect => new ReindirizzamentoPagina(redirect.Id, redirect.SlugOrigine, redirect.CreatoIlUtc))
            .ToListAsync(annullamento);
        var lingueSito = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var mancanti = lingueSito
            .Where(abilitata => abilitata.Codice != codiceLingua && gruppo.All(t => t.CodiceLingua != abilitata.Codice))
            .Select(abilitata => new LinguaDisponibile(abilitata.Codice, abilitata.Nome))
            .ToList();

        return new DettaglioPagina(
            pagina.Id,
            pagina.Slug,
            codiceLingua,
            pagina.Stato,
            pagina.PaginaIniziale,
            bozza.Titolo,
            bozza.Contenuto,
            Convert.ToBase64String(pagina.TokenConcorrenza),
            pagina.IdRevisionePubblicata != pagina.IdUltimaRevisioneBozza,
            revisioni,
            bozza.IdModello,
            bozza.VersioneModello,
            bozza.DatiModello,
            _modelli.Ultimo(bozza.IdModello) is { } recente && recente.Versione > bozza.VersioneModello ? recente.Versione : null,
            gruppo,
            mancanti,
            bozza.TitoloSeo,
            bozza.DescrizioneSeo,
            bozza.NonIndicizzare,
            reindirizzamenti);
    }

    public Task<EsitoOperazione> CreaAsync(Guid idAttore, DatiNuovaPagina dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        return EseguiAsync(idAttore, "Creazione", async attore =>
        {
            if (!RevisionePagina.TentaValidaTesto(dati.Titolo, dati.Contenuto, out var titolo, out var contenuto)
                || !TentaSlug(dati.Slug, titolo, out var slug))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var erroriSeo = ErroriSeo(dati.TitoloSeo, dati.DescrizioneSeo, out var titoloSeo, out var descrizioneSeo);
            if (erroriSeo.Count > 0)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, erroriSeo);
            }

            var idSito = _tenant.IdSito;
            var lingueSito = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
            var codice = string.IsNullOrWhiteSpace(dati.CodiceLingua)
                ? lingueSito.FirstOrDefault(abilitata => abilitata.Predefinita)?.Codice
                : dati.CodiceLingua.Trim().ToLowerInvariant();
            var lingua = lingueSito.FirstOrDefault(abilitata => abilitata.Codice == codice);
            if (lingua is null || await SlugRiservatoDaLinguaAsync(slug, annullamento))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            // Una traduzione si lega a una pagina esistente dello stesso sito, e quella lingua non deve già esserci nel gruppo.
            Guid? idGruppo = null;
            if (dati.IdPaginaOrigine is { } idOrigine)
            {
                var origine = await _contesto.Pagine.AsNoTracking().SingleOrDefaultAsync(candidata => candidata.Id == idOrigine && candidata.IdSito == idSito, annullamento);
                if (origine is null)
                {
                    return new EsitoOperazione(CodiceEsito.NonTrovato);
                }

                if (await _contesto.Pagine.AnyAsync(altra => altra.IdSito == idSito && altra.IdGruppoTraduzione == origine.IdGruppoTraduzione && altra.IdLingua == lingua.IdLingua, annullamento))
                {
                    return new EsitoOperazione(CodiceEsito.GiaPresente);
                }

                idGruppo = origine.IdGruppoTraduzione;
            }

            if (await _contesto.Pagine.AnyAsync(pagina => pagina.IdSito == idSito && pagina.IdLingua == lingua.IdLingua && pagina.Slug == slug, annullamento))
            {
                return new EsitoOperazione(CodiceEsito.GiaPresente);
            }

            if (dati.PaginaIniziale && await EsisteInizialeAsync(idSito, lingua.IdLingua, null, annullamento))
            {
                return new EsitoOperazione(CodiceEsito.PaginaInizialeEsistente);
            }

            var modello = _modelli.Ultimo(string.IsNullOrWhiteSpace(dati.IdModello) ? RevisionePagina.ModelloPredefinito : dati.IdModello.Trim());
            if (modello is null)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Modello"]);
            }

            var errori = _modelli.Valida(modello.Id, modello.Versione, dati.DatiModello, contenuto);
            if (errori.Count > 0)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
            }

            var (pagina, revisione) = Pagina.Crea(
                _tenant.IdTenant,
                idSito,
                lingua.IdLingua,
                slug,
                new ContenutoRevisione(titolo, contenuto, modello.Id, modello.Versione, RegistroModelliPagina.Normalizza(dati.DatiModello), titoloSeo, descrizioneSeo, dati.NonIndicizzare),
                dati.PaginaIniziale,
                attore.Id,
                _orologio,
                idGruppo);

            // Una pagina nuova che prende uno slug lasciato da un'altra pagina vince sul vecchio reindirizzamento.
            _contesto.RedirectPagine.RemoveRange(await _contesto.RedirectPagine
                .Where(redirect => redirect.IdSito == idSito && redirect.IdLingua == lingua.IdLingua && redirect.SlugOrigine == slug)
                .ToListAsync(annullamento));
            _invalidazione.Registra(pagina.IdTenant, pagina.IdSito);
            await _contesto.SalvaNuovaPaginaAsync(pagina, revisione, annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, pagina.Id);
        });
    }

    public Task<EsitoOperazione> SalvaAsync(Guid idAttore, Guid idPagina, DatiModificaPagina dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        return EseguiAsync(idAttore, "Modifica", async attore =>
        {
            if (!RevisionePagina.TentaValidaTesto(dati.Titolo, dati.Contenuto, out var titolo, out var contenuto)
                || !TentaToken(dati.TokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var pagina = await TrovaPaginaAsync(idPagina, annullamento);
            if (pagina is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            var erroriSeo = ErroriSeo(dati.TitoloSeo, dati.DescrizioneSeo, out var titoloSeo, out var descrizioneSeo);
            if (erroriSeo.Count > 0)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, erroriSeo);
            }

            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;

            // Il cambio di slug di una pagina già pubblicata lascia un reindirizzamento 301 dal vecchio indirizzo (C-005).
            string? slugPrecedente = null;
            if (!string.IsNullOrWhiteSpace(dati.Slug) && !string.Equals(dati.Slug.Trim(), pagina.Slug, StringComparison.OrdinalIgnoreCase))
            {
                if (!NormalizzatoreSlug.TentaNormalizza(dati.Slug, out var nuovoSlug) || await SlugRiservatoDaLinguaAsync(nuovoSlug, annullamento))
                {
                    return new EsitoOperazione(CodiceEsito.DatiNonValidi);
                }

                var idSito = _tenant.IdSito;
                if (await _contesto.Pagine.AnyAsync(altra => altra.IdSito == idSito && altra.IdLingua == pagina.IdLingua && altra.Slug == nuovoSlug && altra.Id != pagina.Id, annullamento))
                {
                    return new EsitoOperazione(CodiceEsito.GiaPresente);
                }

                if (!string.Equals(nuovoSlug, pagina.Slug, StringComparison.Ordinal))
                {
                    slugPrecedente = pagina.Slug;
                    pagina.Slug = nuovoSlug;

                    // Se il nuovo slug era un vecchio indirizzo (anche di questa stessa pagina) torna in uso: niente più reindirizzamento da esso.
                    var daTogliere = await _contesto.RedirectPagine
                        .Where(redirect => redirect.IdSito == idSito && redirect.IdLingua == pagina.IdLingua && redirect.SlugOrigine == nuovoSlug)
                        .ToListAsync(annullamento);
                    _contesto.RedirectPagine.RemoveRange(daTogliere);
                    if (pagina.PubblicataInPassato)
                    {
                        _contesto.RedirectPagine.Add(RedirectPagina.Crea(pagina, slugPrecedente, _orologio));
                    }
                }
            }

            if (dati.PaginaIniziale != pagina.PaginaIniziale)
            {
                if (dati.PaginaIniziale && await EsisteInizialeAsync(pagina.IdSito, pagina.IdLingua, pagina.Id, annullamento))
                {
                    return new EsitoOperazione(CodiceEsito.PaginaInizialeEsistente);
                }

                pagina.PaginaIniziale = dati.PaginaIniziale;
            }

            // Il modello (e la sua versione) è quello della bozza: una revisione non cambia mai modello da sola.
            var bozza = await _contesto.RevisioniPagina.AsNoTracking().SingleAsync(revisione => revisione.Id == pagina.IdUltimaRevisioneBozza, annullamento);
            var errori = _modelli.Valida(bozza.IdModello, bozza.VersioneModello, dati.DatiModello, contenuto);
            if (errori.Count > 0)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
            }

            var datiNormalizzati = RegistroModelliPagina.Normalizza(dati.DatiModello);
            var invariati = datiNormalizzati.Count == bozza.DatiModello.Count
                && datiNormalizzati.All(coppia => bozza.DatiModello.TryGetValue(coppia.Key, out var valore) && valore == coppia.Value);
            if (!string.Equals(bozza.Titolo, titolo, StringComparison.Ordinal)
                || !string.Equals(bozza.Contenuto, contenuto, StringComparison.Ordinal)
                || !invariati
                || !string.Equals(bozza.TitoloSeo, titoloSeo, StringComparison.Ordinal)
                || !string.Equals(bozza.DescrizioneSeo, descrizioneSeo, StringComparison.Ordinal)
                || bozza.NonIndicizzare != dati.NonIndicizzare)
            {
                _contesto.RevisioniPagina.Add(pagina.AggiungiRevisione(
                    new ContenutoRevisione(titolo, contenuto, bozza.IdModello, bozza.VersioneModello, datiNormalizzati, titoloSeo, descrizioneSeo, dati.NonIndicizzare),
                    dati.Nota,
                    attore.Id,
                    _orologio));
            }

            // Per una pagina pubblicata che cambia indirizzo l'evento è scritto con la stessa SaveChanges (ADR-0010).
            if (slugPrecedente is not null && pagina.Stato == StatoPagina.Pubblicata)
            {
                _outbox.Accoda(pagina.IdTenant, TipiEventoPagina.Rinominata, new EventoPaginaRinominata(pagina.Id, pagina.IdSito, slugPrecedente, pagina.Slug));
            }

            pagina.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
            _invalidazione.Registra(pagina.IdTenant, pagina.IdSito);
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, pagina.Id);
        });
    }

    public async Task<AnteprimaPagina?> AnteprimaAsync(Guid idAttore, Guid idPagina, Guid? idRevisione, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);

        var pagina = await _contesto.Pagine.AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idPagina && candidata.IdSito == _tenant.IdSito,
            annullamento);
        var idDaMostrare = idRevisione ?? pagina?.IdUltimaRevisioneBozza;
        if (pagina is null || idDaMostrare is null)
        {
            return null;
        }

        var revisione = await _contesto.RevisioniPagina.AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idDaMostrare && candidata.IdPagina == pagina.Id,
            annullamento);
        if (revisione is null)
        {
            return null;
        }

        var codiceLingua = await _contesto.Lingue.Where(lingua => lingua.Id == pagina.IdLingua).Select(lingua => lingua.Codice).SingleAsync(annullamento);
        return new AnteprimaPagina(
            revisione.Titolo,
            revisione.Contenuto,
            codiceLingua,
            revisione.NumeroRevisione,
            revisione.Id == pagina.IdRevisionePubblicata,
            revisione.Id == pagina.IdUltimaRevisioneBozza,
            revisione.IdModello,
            revisione.VersioneModello,
            revisione.DatiModello);
    }

    public Task<EsitoOperazione> PubblicaAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, CancellationToken annullamento) =>
        PubblicaAsync(idAttore, idPagina, null, tokenConcorrenza, annullamento);

    public Task<EsitoOperazione> PubblicaRevisioneAsync(Guid idAttore, Guid idPagina, Guid idRevisione, string? tokenConcorrenza, CancellationToken annullamento) =>
        PubblicaAsync(idAttore, idPagina, idRevisione, tokenConcorrenza, annullamento);

    public Task<EsitoOperazione> RipristinaComeBozzaAsync(Guid idAttore, Guid idPagina, Guid idRevisione, string? tokenConcorrenza, CancellationToken annullamento) =>
        EseguiAsync(idAttore, "Ripristino", async attore =>
        {
            if (!TentaToken(tokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var pagina = await TrovaPaginaAsync(idPagina, annullamento);
            if (pagina is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            var origine = await _contesto.RevisioniPagina.AsNoTracking().SingleOrDefaultAsync(
                candidata => candidata.Id == idRevisione && candidata.IdPagina == pagina.Id,
                annullamento);
            if (origine is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;
            var nuova = pagina.AggiungiRevisione(
                new ContenutoRevisione(origine.Titolo, origine.Contenuto, origine.IdModello, origine.VersioneModello, origine.DatiModello, origine.TitoloSeo, origine.DescrizioneSeo, origine.NonIndicizzare),
                $"Ripristino della revisione {origine.NumeroRevisione}",
                attore.Id,
                _orologio);
            _contesto.RevisioniPagina.Add(nuova);
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, nuova.Id);
        });

    public Task<EsitoOperazione> AggiornaModelloAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, CancellationToken annullamento) =>
        EseguiAsync(idAttore, "AggiornamentoModello", async attore =>
        {
            if (!TentaToken(tokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var pagina = await TrovaPaginaAsync(idPagina, annullamento);
            if (pagina is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            var bozza = await _contesto.RevisioniPagina.AsNoTracking().SingleAsync(revisione => revisione.Id == pagina.IdUltimaRevisioneBozza, annullamento);
            var recente = _modelli.Ultimo(bozza.IdModello);
            var attuale = _modelli.Trova(bozza.IdModello, bozza.VersioneModello);
            if (recente is null || attuale is null || recente.Versione <= attuale.Versione)
            {
                return new EsitoOperazione(CodiceEsito.NonConsentito);
            }

            // Si portano i valori dei campi che esistono ancora; gli altri si scartano.
            var dati = bozza.DatiModello.Where(coppia => recente.Campi.Any(campo => campo.Nome == coppia.Key)).ToDictionary(coppia => coppia.Key, coppia => coppia.Value, StringComparer.Ordinal);
            var errori = _modelli.Valida(recente.Id, recente.Versione, dati, bozza.Contenuto);
            if (errori.Count > 0)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
            }

            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;
            var nuova = pagina.AggiungiRevisione(
                new ContenutoRevisione(bozza.Titolo, bozza.Contenuto, recente.Id, recente.Versione, dati, bozza.TitoloSeo, bozza.DescrizioneSeo, bozza.NonIndicizzare),
                $"Passaggio al modello {recente.Id} versione {recente.Versione}",
                attore.Id,
                _orologio);
            _contesto.RevisioniPagina.Add(nuova);
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, nuova.Id);
        });

    public Task<EsitoOperazione> EliminaReindirizzamentoAsync(Guid idAttore, Guid idPagina, Guid idReindirizzamento, CancellationToken annullamento) =>
        EseguiAsync(idAttore, "EliminazioneReindirizzamento", async _ =>
        {
            var pagina = await TrovaPaginaAsync(idPagina, annullamento);
            if (pagina is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            var redirect = await _contesto.RedirectPagine.SingleOrDefaultAsync(
                candidato => candidato.Id == idReindirizzamento && candidato.IdPagina == pagina.Id,
                annullamento);
            if (redirect is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            _contesto.RedirectPagine.Remove(redirect);
            _invalidazione.Registra(pagina.IdTenant, pagina.IdSito);
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, pagina.Id);
        });

    /// <summary>Pubblica la revisione indicata o, se nulla, l'ultima bozza.</summary>
    private Task<EsitoOperazione> PubblicaAsync(Guid idAttore, Guid idPagina, Guid? idRevisione, string? tokenConcorrenza, CancellationToken annullamento) =>
        EseguiAsync(idAttore, "Pubblicazione", async _ =>
        {
            if (!TentaToken(tokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var pagina = await TrovaPaginaAsync(idPagina, annullamento);
            var idDaPubblicare = idRevisione ?? pagina?.IdUltimaRevisioneBozza;
            if (pagina is null || idDaPubblicare is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            // La revisione deve essere di questa pagina (e quindi dello stesso tenant e sito).
            var revisione = await _contesto.RevisioniPagina.AsNoTracking().SingleOrDefaultAsync(
                candidata => candidata.Id == idDaPubblicare && candidata.IdPagina == pagina.Id,
                annullamento);
            if (revisione is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;
            pagina.Pubblica(revisione, _orologio);
            // L'evento è scritto con la stessa SaveChanges della pubblicazione: o ci sono entrambi o nessuno (ADR-0010).
            _invalidazione.Registra(pagina.IdTenant, pagina.IdSito);
            _outbox.Accoda(pagina.IdTenant, TipiEventoPagina.Pubblicata, new EventoPagina(pagina.Id, pagina.IdSito, pagina.Slug));
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, pagina.Id);
        });

    public Task<EsitoOperazione> RitiraAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, CancellationToken annullamento) =>
        EseguiAsync(idAttore, "Ritiro", async _ =>
        {
            if (!TentaToken(tokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var pagina = await TrovaPaginaAsync(idPagina, annullamento);
            if (pagina is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;
            pagina.Ritira(_orologio);
            _invalidazione.Registra(pagina.IdTenant, pagina.IdSito);
            _outbox.Accoda(pagina.IdTenant, TipiEventoPagina.Ritirata, new EventoPagina(pagina.Id, pagina.IdSito, pagina.Slug));
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, pagina.Id);
        });

    /// <summary>Verifica l'attore ed esegue l'operazione, traducendo i conflitti di concorrenza e di unicità in codici di esito.</summary>
    private async Task<EsitoOperazione> EseguiAsync(Guid idAttore, string azione, Func<Utenza, Task<EsitoOperazione>> operazione)
    {
        var attore = await TrovaAttoreAsync(idAttore, CancellationToken.None);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, azione);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        try
        {
            return await operazione(attore);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
        catch (DbUpdateException)
        {
            return new EsitoOperazione(CodiceEsito.GiaPresente);
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
            "Pagina",
            RegistroAudit.EsitoNegato,
            _tenant.IdTenant,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {azione}",
            CancellationToken.None);
    }

    /// <summary>I codici delle lingue (<c>en</c>, <c>it</c>, ...) sono prefissi di percorso: non si possono usare come slug.</summary>
    private Task<bool> SlugRiservatoDaLinguaAsync(string slug, CancellationToken annullamento) =>
        _contesto.Lingue.AnyAsync(lingua => lingua.Codice == slug, annullamento);

    private Task<Pagina?> TrovaPaginaAsync(Guid idPagina, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return _contesto.Pagine.SingleOrDefaultAsync(candidata => candidata.Id == idPagina && candidata.IdSito == idSito, annullamento);
    }

    private Task<bool> EsisteInizialeAsync(Guid idSito, Guid idLingua, Guid? esclusa, CancellationToken annullamento) =>
        _contesto.Pagine.AnyAsync(
            pagina => pagina.IdSito == idSito && pagina.IdLingua == idLingua && pagina.PaginaIniziale && pagina.Id != esclusa,
            annullamento);

    /// <summary>Normalizza i campi SEO e restituisce i nomi di quelli non validi (nessuno se vanno bene).</summary>
    private static List<string> ErroriSeo(string? titoloSeo, string? descrizioneSeo, out string? titolo, out string? descrizione)
    {
        var errori = new List<string>();
        RevisionePagina.TentaValidaSeo(titoloSeo, null, out titolo, out _);
        RevisionePagina.TentaValidaSeo(null, descrizioneSeo, out _, out descrizione);
        if (!RevisionePagina.TentaValidaSeo(titoloSeo, null, out _, out _))
        {
            errori.Add("TitoloSeo");
        }

        if (!RevisionePagina.TentaValidaSeo(null, descrizioneSeo, out _, out _))
        {
            errori.Add("DescrizioneSeo");
        }

        return errori;
    }

    private static bool TentaSlug(string? slugInserito, string titolo, out string slug) =>
        NormalizzatoreSlug.TentaNormalizza(string.IsNullOrWhiteSpace(slugInserito) ? NormalizzatoreSlug.DaTitolo(titolo) : slugInserito, out slug);

    private static bool TentaToken(string? testo, out byte[] token)
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
}

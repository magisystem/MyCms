using DvCms.Application.Accessi;
using DvCms.Application.Cms;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Cms;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Cms;

public sealed class ServizioPagine : IServizioPagine
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioPagine(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<LinguaDisponibile>> ElencaLingueAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        return await _contesto.Lingue.AsNoTracking().Where(lingua => lingua.Attiva)
            .OrderByDescending(lingua => lingua.Predefinita).ThenBy(lingua => lingua.Nome)
            .Select(lingua => new LinguaDisponibile(lingua.Codice, lingua.Nome))
            .ToListAsync(annullamento);
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
                autore.Email,
                revisione.CreatoIlUtc,
                revisione.Id == pagina.IdRevisionePubblicata,
                revisione.Id == idBozza))
            .ToListAsync(annullamento);
        var bozza = await _contesto.RevisioniPagina.AsNoTracking().SingleAsync(revisione => revisione.Id == idBozza, annullamento);

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
            revisioni);
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

            var codice = string.IsNullOrWhiteSpace(dati.CodiceLingua) ? "it" : dati.CodiceLingua.Trim().ToLowerInvariant();
            var lingua = await _contesto.Lingue.AsNoTracking().SingleOrDefaultAsync(candidata => candidata.Codice == codice && candidata.Attiva, annullamento);
            if (lingua is null)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var idSito = _tenant.IdSito;
            if (await _contesto.Pagine.AnyAsync(pagina => pagina.IdSito == idSito && pagina.IdLingua == lingua.Id && pagina.Slug == slug, annullamento))
            {
                return new EsitoOperazione(CodiceEsito.GiaPresente);
            }

            if (dati.PaginaIniziale && await EsisteInizialeAsync(idSito, lingua.Id, null, annullamento))
            {
                return new EsitoOperazione(CodiceEsito.PaginaInizialeEsistente);
            }

            var (pagina, revisione) = Pagina.Crea(_tenant.IdTenant, idSito, lingua.Id, slug, titolo, contenuto, dati.PaginaIniziale, attore.Id, _orologio);
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

            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;

            // Lo slug di una pagina pubblicata non cambia: il cambio richiederà redirect e canonical (domain-model.md).
            if (!string.IsNullOrWhiteSpace(dati.Slug) && !string.Equals(dati.Slug.Trim(), pagina.Slug, StringComparison.OrdinalIgnoreCase))
            {
                if (pagina.Stato == StatoPagina.Pubblicata)
                {
                    return new EsitoOperazione(CodiceEsito.NonConsentito);
                }

                if (!NormalizzatoreSlug.TentaNormalizza(dati.Slug, out var nuovoSlug))
                {
                    return new EsitoOperazione(CodiceEsito.DatiNonValidi);
                }

                var idSito = _tenant.IdSito;
                if (await _contesto.Pagine.AnyAsync(altra => altra.IdSito == idSito && altra.IdLingua == pagina.IdLingua && altra.Slug == nuovoSlug && altra.Id != pagina.Id, annullamento))
                {
                    return new EsitoOperazione(CodiceEsito.GiaPresente);
                }

                pagina.Slug = nuovoSlug;
            }

            if (dati.PaginaIniziale != pagina.PaginaIniziale)
            {
                if (dati.PaginaIniziale && await EsisteInizialeAsync(pagina.IdSito, pagina.IdLingua, pagina.Id, annullamento))
                {
                    return new EsitoOperazione(CodiceEsito.PaginaInizialeEsistente);
                }

                pagina.PaginaIniziale = dati.PaginaIniziale;
            }

            var bozza = await _contesto.RevisioniPagina.AsNoTracking().SingleAsync(revisione => revisione.Id == pagina.IdUltimaRevisioneBozza, annullamento);
            if (!string.Equals(bozza.Titolo, titolo, StringComparison.Ordinal) || !string.Equals(bozza.Contenuto, contenuto, StringComparison.Ordinal))
            {
                _contesto.RevisioniPagina.Add(pagina.AggiungiRevisione(titolo, contenuto, dati.Nota, attore.Id, _orologio));
            }

            pagina.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, pagina.Id);
        });
    }

    public Task<EsitoOperazione> PubblicaAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, CancellationToken annullamento) =>
        EseguiAsync(idAttore, "Pubblicazione", async _ =>
        {
            if (!TentaToken(tokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi);
            }

            var pagina = await TrovaPaginaAsync(idPagina, annullamento);
            if (pagina?.IdUltimaRevisioneBozza is not { } idBozza)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }

            _contesto.Entry(pagina).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;
            var bozza = await _contesto.RevisioniPagina.AsNoTracking().SingleAsync(revisione => revisione.Id == idBozza, annullamento);
            pagina.Pubblica(bozza, _orologio);
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

    private Task<Pagina?> TrovaPaginaAsync(Guid idPagina, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return _contesto.Pagine.SingleOrDefaultAsync(candidata => candidata.Id == idPagina && candidata.IdSito == idSito, annullamento);
    }

    private Task<bool> EsisteInizialeAsync(Guid idSito, Guid idLingua, Guid? esclusa, CancellationToken annullamento) =>
        _contesto.Pagine.AnyAsync(
            pagina => pagina.IdSito == idSito && pagina.IdLingua == idLingua && pagina.PaginaIniziale && pagina.Id != esclusa,
            annullamento);

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

using DvCms.Application.Accessi;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Accessi;
using DvCms.Domain.Localizzazione;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Localizzazione;

public sealed class ServizioGestioneTesti : IServizioGestioneTesti
{
    private readonly ContestoDatabase _contesto;
    private readonly IServizioAudit _audit;
    private readonly CacheTestiStatici _cache;
    private readonly TimeProvider _orologio;

    public ServizioGestioneTesti(ContestoDatabase contesto, IServizioAudit audit, CacheTestiStatici cache, TimeProvider orologio)
    {
        _contesto = contesto;
        _audit = audit;
        _cache = cache;
        _orologio = orologio;
    }

    public async Task<PaginaTesti> ElencaAsync(Guid idAttore, FiltroTesti filtro, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        if (!await EPlatformAdminAsync(idAttore, annullamento))
        {
            throw new UnauthorizedAccessException("Operazione riservata al PlatformAdmin.");
        }

        var query =
            from testo in _contesto.TestiStatici.AsNoTracking()
            join lingua in _contesto.Lingue.AsNoTracking() on testo.IdLingua equals lingua.Id
            select new { testo, lingua.Codice };

        if (!string.IsNullOrWhiteSpace(filtro.CodiceLingua))
        {
            var codice = filtro.CodiceLingua.Trim().ToLowerInvariant();
            query = query.Where(riga => riga.Codice == codice);
        }

        if (filtro.SoloDaTradurre)
        {
            query = query.Where(riga => riga.testo.DaTradurre);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Ricerca))
        {
            var ricerca = filtro.Ricerca.Trim();
            query = query.Where(riga => riga.testo.Chiave.Contains(ricerca) || riga.testo.Testo.Contains(ricerca));
        }

        var totale = await query.CountAsync(annullamento);
        var totalePagine = Math.Max(1, (int)Math.Ceiling(totale / (double)IServizioGestioneTesti.DimensionePagina));
        var pagina = Math.Clamp(filtro.Pagina, 1, totalePagine);

        var righe = await query
            .OrderBy(riga => riga.testo.Chiave).ThenBy(riga => riga.Codice)
            .Skip((pagina - 1) * IServizioGestioneTesti.DimensionePagina)
            .Take(IServizioGestioneTesti.DimensionePagina)
            .Select(riga => new TestoGestito(riga.testo.Id, riga.Codice, riga.testo.Chiave, riga.testo.Testo, riga.testo.DaTradurre, riga.testo.AggiornatoIlUtc))
            .ToListAsync(annullamento);

        return new PaginaTesti(righe, pagina, totalePagine, totale);
    }

    public async Task<EsitoOperazione> ModificaAsync(Guid idAttore, Guid idTesto, string? testo, CancellationToken annullamento)
    {
        if (!await EPlatformAdminAsync(idAttore, annullamento))
        {
            var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore, annullamento);
            await _audit.RegistraAsync(
                AzioniAudit.OperazioneNegata,
                "TestoStatico",
                RegistroAudit.EsitoNegato,
                null,
                attoreEsiste ? idAttore : null,
                $"azione richiesta: {AzioniAudit.ModificaTestoStatico}",
                annullamento);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!TentaTestoSemplice(testo, out var valido))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi);
        }

        var riga = await _contesto.TestiStatici.SingleOrDefaultAsync(candidato => candidato.Id == idTesto, annullamento);
        if (riga is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var codiceLingua = await _contesto.Lingue.Where(lingua => lingua.Id == riga.IdLingua).Select(lingua => lingua.Codice).SingleAsync(annullamento);
        riga.Testo = valido;
        riga.DaTradurre = false;
        riga.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        riga.AggiornatoDaIdUtenza = idAttore;

        // Audit e modifica nella stessa SaveChanges. Nell'audit non si copia il testo: solo lingua e identificativo.
        await _audit.RegistraAsync(
            AzioniAudit.ModificaTestoStatico,
            "TestoStatico",
            RegistroAudit.EsitoRiuscito,
            null,
            idAttore,
            $"lingua {codiceLingua}, testo {riga.Id}",
            annullamento);

        // La cache si aggiorna solo dopo il salvataggio; altre istanze si allineano al prossimo caricamento (ADR-0018).
        _cache.Imposta(codiceLingua, riga.Chiave, valido);
        return new EsitoOperazione(CodiceEsito.Riuscito, riga.Id);
    }

    private Task<bool> EPlatformAdminAsync(Guid idAttore, CancellationToken annullamento) =>
        _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAttore && candidata.Ruolo == RuoloUtenza.PlatformAdmin && candidata.Stato == StatoUtenza.Attiva,
            annullamento);

    /// <summary>Testo semplice: non vuoto, entro il limite, senza caratteri di controllo salvo a capo e tabulazione.</summary>
    private static bool TentaTestoSemplice(string? testo, out string valido)
    {
        valido = testo?.Trim() ?? string.Empty;
        return valido.Length is > 0 and <= TestoStatico.LunghezzaMassimaTesto
            && !valido.Any(carattere => char.IsControl(carattere) && carattere is not ('\n' or '\r' or '\t'));
    }
}

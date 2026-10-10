using DvCms.Application.Accessi;
using DvCms.Application.Outbox;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Accessi;
using DvCms.Domain.Outbox;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Outbox;

/// <summary>
/// Gestione della coda (vedi <see cref="IGestioneCodaOutbox"/>). Caso d'uso privilegiato: legge e scrive messaggi di tutti i
/// tenant con <c>IgnoreQueryFilters()</c>, quindi riverifica a ogni operazione che l'attore sia un <c>PlatformAdmin</c> attivo.
/// </summary>
public sealed class ServizioCodaOutbox : IGestioneCodaOutbox
{
    private readonly ContestoDatabase _contesto;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioCodaOutbox(ContestoDatabase contesto, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<RiepilogoCoda> RiepilogoAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);

        var messaggi = _contesto.MessaggiOutbox.IgnoreQueryFilters().AsNoTracking();
        var pendenti = await messaggi.CountAsync(m => m.Stato == StatoMessaggioOutbox.Pendente, annullamento);
        var falliti = await messaggi.CountAsync(m => m.Stato == StatoMessaggioOutbox.Fallito, annullamento);
        var completati = await messaggi.CountAsync(m => m.Stato == StatoMessaggioOutbox.Completato, annullamento);
        var piuVecchio = await messaggi.Where(m => m.Stato == StatoMessaggioOutbox.Pendente).MinAsync(m => (DateTime?)m.CreatoIlUtc, annullamento);
        return new RiepilogoCoda(pendenti, falliti, completati, piuVecchio);
    }

    public async Task<IReadOnlyList<MessaggioInCoda>> ElencaAsync(Guid idAttore, StatoMessaggioOutbox? stato, int massimo, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);

        var limite = Math.Clamp(massimo, 1, 500);
        var query = _contesto.MessaggiOutbox.IgnoreQueryFilters().AsNoTracking();
        if (stato is { } filtro)
        {
            query = query.Where(m => m.Stato == filtro);
        }

        return await (
            from messaggio in query
            join tenant in _contesto.Tenant.IgnoreQueryFilters().AsNoTracking() on messaggio.IdTenant equals tenant.Id
            orderby messaggio.CreatoIlUtc descending
            select new MessaggioInCoda(
                messaggio.Id,
                messaggio.IdTenant,
                tenant.Nome,
                messaggio.Tipo,
                messaggio.Stato,
                messaggio.Tentativi,
                messaggio.CreatoIlUtc,
                messaggio.ProssimoTentativoIlUtc,
                messaggio.ElaboratoIlUtc,
                messaggio.UltimoErrore))
            .Take(limite)
            .ToListAsync(annullamento);
    }

    public async Task<EsitoOperazione> RitentaAsync(Guid idAttore, Guid idMessaggio, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.RitentativoMessaggio, annullamento);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;

        // Aggiornamento condizionato allo stato: due richieste insieme, o un messaggio non fallito, non cambiano nulla.
        var modificati = await _contesto.MessaggiOutbox.IgnoreQueryFilters()
            .Where(m => m.Id == idMessaggio && m.Stato == StatoMessaggioOutbox.Fallito)
            .ExecuteUpdateAsync(
                aggiornamento => aggiornamento
                    .SetProperty(m => m.Stato, StatoMessaggioOutbox.Pendente)
                    .SetProperty(m => m.Tentativi, 0)
                    .SetProperty(m => m.ProssimoTentativoIlUtc, adesso)
                    .SetProperty(m => m.BloccatoDa, (Guid?)null)
                    .SetProperty(m => m.BloccatoFinoAlUtc, (DateTime?)null),
                annullamento);
        return await EsitoAsync(modificati, idMessaggio, attore, AzioniAudit.RitentativoMessaggio, annullamento);
    }

    public async Task<EsitoOperazione> ScartaAsync(Guid idAttore, Guid idMessaggio, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.ScartoMessaggio, annullamento);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var eliminati = await _contesto.MessaggiOutbox.IgnoreQueryFilters()
            .Where(m => m.Id == idMessaggio && m.Stato == StatoMessaggioOutbox.Fallito)
            .ExecuteDeleteAsync(annullamento);
        return await EsitoAsync(eliminati, idMessaggio, attore, AzioniAudit.ScartoMessaggio, annullamento);
    }

    private async Task<EsitoOperazione> EsitoAsync(int righe, Guid idMessaggio, Utenza attore, string azione, CancellationToken annullamento)
    {
        if (righe == 0)
        {
            // O non esiste, o non è fallito: chi opera dal menu non ha bisogno di distinguere i due casi.
            var esiste = await _contesto.MessaggiOutbox.IgnoreQueryFilters().AnyAsync(m => m.Id == idMessaggio, annullamento);
            return new EsitoOperazione(esiste ? CodiceEsito.NonConsentito : CodiceEsito.NonTrovato);
        }

        await _audit.RegistraAsync(azione, $"MessaggioOutbox:{idMessaggio}", RegistroAudit.EsitoRiuscito, null, attore.Id, null, annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idMessaggio);
    }

    private async Task RichiediAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata al PlatformAdmin.");
        }
    }

    private Task<Utenza?> TrovaAttoreAsync(Guid idAttore, CancellationToken annullamento) =>
        _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore && candidata.Ruolo == RuoloUtenza.PlatformAdmin && candidata.Stato == StatoUtenza.Attiva,
            annullamento);

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione, CancellationToken annullamento)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore, annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "MessaggioOutbox",
            RegistroAudit.EsitoNegato,
            null,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {azione}",
            annullamento);
    }
}

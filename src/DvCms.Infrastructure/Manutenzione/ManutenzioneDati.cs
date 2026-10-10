using DvCms.Domain.Accessi;
using DvCms.Domain.Outbox;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Manutenzione;

/// <summary>Parametri della manutenzione (configurazione <c>Manutenzione:*</c>).</summary>
public sealed record OpzioniManutenzione(
    bool Abilitata,
    TimeSpan Intervallo,
    TimeSpan ConservazioneCompletati,
    TimeSpan ConservazioneFalliti,
    TimeSpan ConservazioneAccessi,
    int DimensioneLotto)
{
    public static OpzioniManutenzione Predefinite { get; } =
        new(true, TimeSpan.FromHours(1), TimeSpan.FromDays(7), TimeSpan.FromDays(90), TimeSpan.FromDays(30), 1000);
}

/// <summary>
/// Elimina le righe che non servono più (G-001): messaggi dell'outbox completati o falliti da tempo, richieste di recupero
/// password e inviti conclusi o scaduti. Procede a lotti per non tenere blocchi lunghi sulle tabelle e può essere eseguita da
/// più istanze insieme senza danni (ogni cancellazione è un'istruzione sola e idempotente).
/// </summary>
public sealed partial class ManutenzioneDati
{
    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly OpzioniManutenzione _opzioni;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ManutenzioneDati> _logger;

    public ManutenzioneDati(IServiceScopeFactory fabbricaAmbiti, OpzioniManutenzione opzioni, TimeProvider orologio, ILogger<ManutenzioneDati> logger)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _opzioni = opzioni;
        _orologio = orologio;
        _logger = logger;
    }

    /// <summary>Esegue un passaggio e restituisce quante righe ha eliminato in tutto.</summary>
    public async Task<int> EseguiAsync(CancellationToken annullamento)
    {
        await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var lotto = _opzioni.DimensioneLotto;

        var completati = adesso - _opzioni.ConservazioneCompletati;
        var falliti = adesso - _opzioni.ConservazioneFalliti;
        var accessi = adesso - _opzioni.ConservazioneAccessi;

        var totale = 0;
        totale += await RipulisciAsync(
            "MessaggiOutbox",
            () => contesto.MessaggiOutbox.IgnoreQueryFilters()
                .Where(m => (m.Stato == StatoMessaggioOutbox.Completato && m.ElaboratoIlUtc < completati)
                    || (m.Stato == StatoMessaggioOutbox.Fallito && m.CreatoIlUtc < falliti))
                .Take(lotto)
                .ExecuteDeleteAsync(annullamento),
            annullamento);

        // Le richieste di recupero valgono al massimo qualche decina di minuti; si tengono più a lungo solo per i limiti orari.
        totale += await RipulisciAsync(
            "RichiesteRecuperoPassword",
            () => contesto.RichiesteRecuperoPassword.IgnoreQueryFilters()
                .Where(r => r.CreatoIlUtc < accessi)
                .Take(lotto)
                .ExecuteDeleteAsync(annullamento),
            annullamento);

        totale += await RipulisciAsync(
            "InvitiAccount",
            () => contesto.InvitiAccount.IgnoreQueryFilters()
                .Where(i => (i.Stato != StatoInvito.Inviato && i.CreatoIlUtc < accessi) || i.ScadeIlUtc < accessi)
                .Take(lotto)
                .ExecuteDeleteAsync(annullamento),
            annullamento);

        // Il registro delle invalidazioni serve alle istanze per pochi secondi: due ore coprono anche un riavvio lento.
        var invalidazioni = adesso - TimeSpan.FromHours(2);
        totale += await RipulisciAsync(
            "InvalidazioniCache",
            () => contesto.InvalidazioniCache.IgnoreQueryFilters()
                .Where(i => i.CreatoIlUtc < invalidazioni)
                .Take(lotto)
                .ExecuteDeleteAsync(annullamento),
            annullamento);

        if (totale > 0)
        {
            RegistraRipulitura(_logger, totale);
        }

        return totale;
    }

    private async Task<int> RipulisciAsync(string tabella, Func<Task<int>> eliminaLotto, CancellationToken annullamento)
    {
        var totale = 0;
        int eliminati;
        do
        {
            annullamento.ThrowIfCancellationRequested();
            eliminati = await eliminaLotto();
            totale += eliminati;
            MetricheOutbox.Ripulite(tabella, eliminati);
        }
        while (eliminati >= _opzioni.DimensioneLotto);

        return totale;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Manutenzione: eliminate {Righe} righe scadute")]
    private static partial void RegistraRipulitura(ILogger logger, int righe);
}

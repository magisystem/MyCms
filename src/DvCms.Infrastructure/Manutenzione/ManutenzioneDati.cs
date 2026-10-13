using DvCms.Contracts.Negozio;
using DvCms.Core.Outbox;
using DvCms.Data;
using DvCms.DTO.Accessi;
using DvCms.DTO.Manutenzione;
using DvCms.Infrastructure.Outbox;
using DvCms.Models.Accessi;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Manutenzione;

/// <summary>
/// Elimina le righe che non servono più (G-001): messaggi dell'outbox completati o falliti da tempo, richieste di recupero
/// password e inviti conclusi o scaduti. Procede a lotti per non tenere blocchi lunghi sulle tabelle e può essere eseguita da
/// più istanze insieme senza danni (ogni cancellazione è un'istruzione sola e idempotente).
/// </summary>
public sealed partial class ManutenzioneDati
{
    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly OpzioniManutenzione _opzioni;
    private readonly OpzioniAcquirenti _opzioniAcquirenti;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ManutenzioneDati> _logger;

    public ManutenzioneDati(IServiceScopeFactory fabbricaAmbiti, OpzioniManutenzione opzioni, OpzioniAcquirenti opzioniAcquirenti, TimeProvider orologio, ILogger<ManutenzioneDati> logger)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _opzioni = opzioni;
        _opzioniAcquirenti = opzioniAcquirenti;
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

        // Registrazioni di acquirenti mai confermate: l'account in attesa (e, a cascata, i suoi link) si toglie dopo qualche giorno,
        // così l'indirizzo torna libero; le conferme concluse o scadute si tengono come gli inviti.
        var nonConfermati = adesso - _opzioniAcquirenti.ConservazioneNonConfermati;
        totale += await RipulisciAsync(
            "UtenzeNonConfermate",
            () => contesto.Utenze.IgnoreQueryFilters()
                .Where(u => u.Ruolo == RuoloUtenza.Utente && u.Stato == StatoUtenza.InAttesaDiConferma && u.CreatoIlUtc < nonConfermati)
                .Take(lotto)
                .ExecuteDeleteAsync(annullamento),
            annullamento);
        totale += await RipulisciAsync(
            "ConfermeEmailUtenza",
            () => contesto.ConfermeEmailUtenza.IgnoreQueryFilters()
                .Where(c => (c.Stato != StatoConfermaEmail.Inviata && c.CreatoIlUtc < accessi) || c.ScadeIlUtc < accessi)
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

        // Prenotazioni di giacenza scadute: liberano la merce e annullano gli ordini che non sono stati pagati.
        totale += await ambito.ServiceProvider.GetRequiredService<IServizioGiacenze>().RilasciaScaduteAsync(annullamento);

        // Fine conservazione degli ordini (A7): si cancella la copia dei dati personali, restano importi e righe.
        totale += await ambito.ServiceProvider.GetRequiredService<IServizioDatiPersonali>().AnonimizzaOrdiniScadutiAsync(annullamento);

        // Incassi mai notificati (ritorno del cliente perso, webhook non arrivato): si rilegge lo stato presso il provider. Non incassa nulla.
        totale += await ambito.ServiceProvider.GetRequiredService<Pagamenti.RiconciliazionePagamenti>().EseguiAsync(annullamento);

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

using DvCms.Contracts.Outbox;
using DvCms.Core.Outbox;
using DvCms.Data;
using DvCms.Data.Outbox;
using DvCms.DTO.Outbox;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DvCms.Services.Outbox;

/// <summary>
/// Elabora un lotto di messaggi: li prenota con un aggiornamento atomico (più worker non si pestano i piedi), li passa
/// al gestore del loro tipo e registra l'esito. Un errore fa ritentare con attesa crescente (10 s, 20 s, 40 s, ... al massimo
/// un'ora) fino al limite di tentativi, poi il messaggio diventa <see cref="StatoMessaggioOutbox.Fallito"/>.
/// </summary>
public sealed partial class ElaboratoreOutbox
{
    private static readonly TimeSpan _attesaBase = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _attesaMassima = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly OpzioniOutbox _opzioni;
    private readonly IPrenotatoreMessaggiOutbox _prenotatore;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ElaboratoreOutbox> _logger;

    public ElaboratoreOutbox(IServiceScopeFactory fabbricaAmbiti, OpzioniOutbox opzioni, IPrenotatoreMessaggiOutbox prenotatore, TimeProvider orologio, ILogger<ElaboratoreOutbox> logger)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _opzioni = opzioni;
        _prenotatore = prenotatore;
        _orologio = orologio;
        _logger = logger;
    }

    /// <summary>Elabora un lotto e restituisce quanti messaggi sono stati prelevati.</summary>
    public async Task<int> ElaboraLottoAsync(CancellationToken annullamento)
    {
        var worker = Guid.NewGuid();
        var prelevati = await PrenotaAsync(worker, annullamento);
        foreach (var messaggio in prelevati)
        {
            await ElaboraAsync(messaggio, annullamento);
        }

        return prelevati.Count;
    }

    public static TimeSpan AttesaPerTentativo(int tentativiEffettuati)
    {
        var potenza = Math.Min(Math.Max(tentativiEffettuati - 1, 0), 20);
        var ticks = _attesaBase.Ticks * (1L << potenza);
        return TimeSpan.FromTicks(Math.Min(ticks, _attesaMassima.Ticks));
    }

    private async Task<List<MessaggioOutbox>> PrenotaAsync(Guid worker, CancellationToken annullamento)
    {
        await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var scadenza = adesso + _opzioni.DurataPrenotazione;

        var prenotati = await _prenotatore.PrenotaAsync(contesto, _opzioni.DimensioneLotto, adesso, scadenza, worker, annullamento);
        if (prenotati.Count == 0)
        {
            return [];
        }

        return await contesto.MessaggiOutbox.IgnoreQueryFilters().AsNoTracking()
            .Where(messaggio => prenotati.Contains(messaggio.Id))
            .OrderBy(messaggio => messaggio.ProssimoTentativoIlUtc)
            .ToListAsync(annullamento);
    }

    private async Task ElaboraAsync(MessaggioOutbox messaggio, CancellationToken annullamento)
    {
        string? errore = null;
        var definitivo = false;
        try
        {
            await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
            var gestore = ambito.ServiceProvider.GetServices<IGestoreOutbox>().FirstOrDefault(candidato => candidato.Tipo == messaggio.Tipo);
            if (gestore is null)
            {
                errore = $"Nessun gestore per il tipo '{messaggio.Tipo}'.";
                definitivo = true;
            }
            else
            {
                await gestore.GestisciAsync(
                    new MessaggioDaElaborare(messaggio.Id, messaggio.IdTenant, messaggio.Tipo, messaggio.Payload, messaggio.Tentativi + 1),
                    annullamento);
            }
        }
        catch (OperationCanceledException) when (annullamento.IsCancellationRequested)
        {
            // Arresto dell'applicazione: la prenotazione scade da sola e il messaggio sarà ripreso.
            throw;
        }
        catch (Exception eccezione)
        {
            errore = Tronca($"{eccezione.GetType().Name}: {eccezione.Message}");
            RegistraErrore(_logger, messaggio.Id, messaggio.Tipo, eccezione.GetType().Name);
        }

        await RegistraEsitoAsync(messaggio, errore, definitivo, annullamento);
    }

    private async Task RegistraEsitoAsync(MessaggioOutbox messaggio, string? errore, bool definitivo, CancellationToken annullamento)
    {
        await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var query = contesto.MessaggiOutbox.IgnoreQueryFilters().Where(candidato => candidato.Id == messaggio.Id);

        if (errore is null)
        {
            MetricheOutbox.Elaborato(messaggio.Tipo, "riuscito");
            await query.ExecuteUpdateAsync(
                aggiornamento => aggiornamento
                    .SetProperty(candidato => candidato.Stato, StatoMessaggioOutbox.Completato)
                    .SetProperty(candidato => candidato.Tentativi, messaggio.Tentativi + 1)
                    .SetProperty(candidato => candidato.ElaboratoIlUtc, adesso)
                    .SetProperty(candidato => candidato.UltimoErrore, (string?)null)
                    .SetProperty(candidato => candidato.BloccatoDa, (Guid?)null)
                    .SetProperty(candidato => candidato.BloccatoFinoAlUtc, (DateTime?)null),
                annullamento);
            return;
        }

        var tentativi = messaggio.Tentativi + 1;
        var esaurito = definitivo || tentativi >= _opzioni.TentativiMassimi;
        var prossimo = adesso + AttesaPerTentativo(tentativi);
        MetricheOutbox.Elaborato(messaggio.Tipo, esaurito ? "fallito" : "ritentato");
        await query.ExecuteUpdateAsync(
            aggiornamento => aggiornamento
                .SetProperty(candidato => candidato.Stato, esaurito ? StatoMessaggioOutbox.Fallito : StatoMessaggioOutbox.Pendente)
                .SetProperty(candidato => candidato.Tentativi, tentativi)
                .SetProperty(candidato => candidato.ProssimoTentativoIlUtc, prossimo)
                .SetProperty(candidato => candidato.UltimoErrore, errore)
                .SetProperty(candidato => candidato.BloccatoDa, (Guid?)null)
                .SetProperty(candidato => candidato.BloccatoFinoAlUtc, (DateTime?)null),
            annullamento);
    }

    private static string Tronca(string testo) =>
        testo.Length > MessaggioOutbox.LunghezzaMassimaErrore ? testo[..MessaggioOutbox.LunghezzaMassimaErrore] : testo;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox: elaborazione del messaggio {IdMessaggio} di tipo {Tipo} non riuscita ({TipoEccezione})")]
    private static partial void RegistraErrore(ILogger logger, Guid idMessaggio, string tipo, string tipoEccezione);
}

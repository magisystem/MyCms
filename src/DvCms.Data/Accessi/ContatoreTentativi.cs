using DvCms.Data;
using DvCms.DTO.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Data.Accessi;

/// <summary>
/// Contatore dei tentativi falliti di un account, condiviso da password e secondo fattore: chi indovina un codice a forza bruta
/// consuma lo stesso budget di chi indovina la password. Aggiornamenti atomici sul database (<c>Contatore = Contatore + 1</c>):
/// con molte richieste parallele ogni tentativo viene contato, invece di perderne tutti tranne uno per il conflitto di
/// concorrenza. Il blocco scatta con un secondo aggiornamento condizionato alla soglia, anch'esso atomico.
/// </summary>
public sealed partial class ContatoreTentativi
{
    private readonly ContestoDatabase _contesto;
    private readonly OpzioniSicurezza _opzioni;
    private readonly ILogger<ContatoreTentativi> _logger;

    public ContatoreTentativi(ContestoDatabase contesto, OpzioniSicurezza opzioni, ILogger<ContatoreTentativi> logger)
    {
        _contesto = contesto;
        _opzioni = opzioni;
        _logger = logger;
    }

    public async Task RegistraFallimentoAsync(Guid idUtenza, DateTime adesso, CancellationToken annullamento)
    {
        var soglia = _opzioni.TentativiMassimi;
        var bloccoFinoAl = adesso + _opzioni.DurataBlocco;

        await _contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza).ExecuteUpdateAsync(
            aggiornamento => aggiornamento
                .SetProperty(u => u.ContatoreAccessiFalliti, u => u.ContatoreAccessiFalliti + 1)
                .SetProperty(u => u.AggiornatoIlUtc, adesso),
            annullamento);
        var bloccati = await _contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza && u.ContatoreAccessiFalliti >= soglia).ExecuteUpdateAsync(
            aggiornamento => aggiornamento
                .SetProperty(u => u.BloccatoFinoAlUtc, bloccoFinoAl)
                .SetProperty(u => u.ContatoreAccessiFalliti, 0),
            annullamento);
        if (bloccati > 0)
        {
            RegistraBlocco(_logger, idUtenza);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account bloccato per 5 minuti dopo troppi tentativi falliti: {IdUtenza}")]
    private static partial void RegistraBlocco(ILogger logger, Guid idUtenza);
}

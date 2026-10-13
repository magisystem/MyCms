namespace DvCms.Contracts.Manutenzione;

/// <summary>
/// Un'attività periodica di manutenzione fornita da un progetto di servizi (ADR-0024). Il servizio di manutenzione le esegue tutte,
/// in ordine di registrazione, ad ogni passaggio: così non deve conoscere i progetti che le implementano.
/// </summary>
public interface IAttivitaManutenzione
{
    /// <summary>Esegue l'attività e restituisce il numero di elementi trattati (per il registro).</summary>
    Task<int> EseguiAsync(CancellationToken annullamento);
}

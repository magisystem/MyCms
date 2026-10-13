namespace DvCms.Contracts.Persistenza;

/// <summary>
/// Riconosce gli errori del motore di database che il codice deve trattare in modo particolare. Dipendono dal motore, quindi li
/// fornisce il progetto del provider (ADR-0024): per SQL Server sono gli errori 1205 (deadlock) e 2601 o 2627 (unicità violata).
/// </summary>
public interface IRilevatoreErroriDatabase
{
    /// <summary>Vero se l'eccezione (o una delle sue interne) è un deadlock: la transazione vittima è già annullata per intero.</summary>
    bool EDeadlock(Exception eccezione);

    /// <summary>Vero se l'eccezione (o una delle sue interne) è la violazione di un indice o vincolo di unicità.</summary>
    bool EViolazioneUnicita(Exception eccezione);
}

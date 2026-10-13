namespace DvCms.Contracts.Persistenza;

/// <summary>
/// Riconosce un errore di deadlock del motore di database (la transazione vittima è già annullata per intero). Dipende dal motore, quindi
/// lo fornisce il progetto del provider (ADR-0024): per SQL Server è l'errore 1205.
/// </summary>
public interface IRilevatoreDeadlock
{
    /// <summary>Vero se l'eccezione (o una delle sue interne) è un deadlock.</summary>
    bool EDeadlock(Exception eccezione);
}

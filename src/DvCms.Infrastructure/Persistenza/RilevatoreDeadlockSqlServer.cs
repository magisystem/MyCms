using DvCms.Contracts.Persistenza;
using Microsoft.Data.SqlClient;

namespace DvCms.Infrastructure.Persistenza;

/// <summary>Deadlock di SQL Server: errore 1205 (la transazione vittima è già annullata per intero).</summary>
public sealed class RilevatoreDeadlockSqlServer : IRilevatoreDeadlock
{
    private const int CodiceDeadlock = 1205;

    public bool EDeadlock(Exception eccezione)
    {
        ArgumentNullException.ThrowIfNull(eccezione);
        return eccezione is SqlException { Number: CodiceDeadlock } || (eccezione.InnerException is { } interna && EDeadlock(interna));
    }
}

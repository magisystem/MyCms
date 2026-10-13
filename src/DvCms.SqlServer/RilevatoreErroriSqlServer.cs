using DvCms.Contracts.Persistenza;
using Microsoft.Data.SqlClient;

namespace DvCms.SqlServer;

/// <summary>Errori di SQL Server: 1205 per il deadlock, 2601 e 2627 per l'unicità violata.</summary>
public sealed class RilevatoreErroriSqlServer : IRilevatoreErroriDatabase
{
    private const int CodiceDeadlock = 1205;
    private const int CodiceIndiceUnico = 2601;
    private const int CodiceVincoloUnico = 2627;

    public bool EDeadlock(Exception eccezione) => Contiene(eccezione, numero => numero == CodiceDeadlock);

    public bool EViolazioneUnicita(Exception eccezione) => Contiene(eccezione, numero => numero is CodiceIndiceUnico or CodiceVincoloUnico);

    private static bool Contiene(Exception eccezione, Func<int, bool> corrisponde)
    {
        ArgumentNullException.ThrowIfNull(eccezione);
        for (var corrente = eccezione; corrente is not null; corrente = corrente.InnerException)
        {
            if (corrente is SqlException sql && corrisponde(sql.Number))
            {
                return true;
            }
        }

        return false;
    }
}

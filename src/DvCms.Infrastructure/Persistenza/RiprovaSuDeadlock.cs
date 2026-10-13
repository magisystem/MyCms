using Microsoft.Data.SqlClient;

namespace DvCms.Infrastructure.Persistenza;

/// <summary>
/// SQL Server può scegliere una transazione come vittima di un deadlock (errore 1205) quando più richieste toccano le stesse righe
/// in ordine diverso — ad esempio il ritorno del cliente da PayPal e il webhook sullo stesso ordine. La transazione vittima è già
/// annullata per intero: ripetere l'operazione è sicuro se l'operazione è idempotente. Si ripete solo fuori da una transazione
/// esterna (dentro una non si può ricominciare) e per un numero limitato di volte.
/// </summary>
internal static class RiprovaSuDeadlock
{
    private const int CodiceDeadlock = 1205;
    private const int TentativiMassimi = 4;

    public static async Task<T> EseguiAsync<T>(ContestoDatabase contesto, Func<Task<T>> operazione)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(operazione);

        for (var tentativo = 1; ; tentativo++)
        {
            try
            {
                return await operazione();
            }
            catch (Exception eccezione) when (tentativo < TentativiMassimi && contesto.Database.CurrentTransaction is null && EDeadlock(eccezione))
            {
                // Lo stato in memoria può riferirsi a righe mai salvate: si riparte puliti, con una breve attesa diversa per ogni tentativo.
                contesto.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * tentativo * Random.Shared.Next(1, 4)));
            }
        }
    }

    private static bool EDeadlock(Exception eccezione) =>
        eccezione is SqlException { Number: CodiceDeadlock } || (eccezione.InnerException is { } interna && EDeadlock(interna));
}

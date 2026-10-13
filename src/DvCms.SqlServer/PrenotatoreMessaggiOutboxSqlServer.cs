using DvCms.Data;
using DvCms.Data.Outbox;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;

namespace DvCms.SqlServer;

/// <summary>
/// Prenotazione atomica con il modello classico delle code su SQL Server: <c>UPDLOCK</c> e <c>READPAST</c> fanno saltare le righe già prese da
/// altri worker, senza attese né deadlock. Lo stato è salvato come testo (vedi la configurazione).
/// </summary>
public sealed class PrenotatoreMessaggiOutboxSqlServer : IPrenotatoreMessaggiOutbox
{
    public async Task<IReadOnlyList<Guid>> PrenotaAsync(ContestoDatabase contesto, int dimensioneLotto, DateTime adesso, DateTime scadenza, Guid worker, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var pendente = nameof(StatoMessaggioOutbox.Pendente);
        return await contesto.Database.SqlQuery<Guid>(
            $"""
            WITH candidati AS (
                SELECT TOP ({dimensioneLotto}) *
                FROM MessaggiOutbox WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Stato = {pendente}
                    AND ProssimoTentativoIlUtc <= {adesso}
                    AND (BloccatoFinoAlUtc IS NULL OR BloccatoFinoAlUtc < {adesso})
                ORDER BY ProssimoTentativoIlUtc
            )
            UPDATE candidati SET BloccatoDa = {worker}, BloccatoFinoAlUtc = {scadenza}
            OUTPUT inserted.Id AS Value
            """)
            .ToListAsync(annullamento);
    }
}

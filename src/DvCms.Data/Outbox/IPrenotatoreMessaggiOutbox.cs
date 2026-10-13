namespace DvCms.Data.Outbox;

/// <summary>
/// Prenota un lotto di messaggi dell'outbox da elaborare, in modo che più worker non prendano gli stessi. Il modo di farlo dipende dal motore
/// (per SQL Server <c>UPDLOCK, READPAST</c> e <c>OUTPUT</c>), quindi lo fornisce il progetto del provider (ADR-0024).
/// </summary>
public interface IPrenotatoreMessaggiOutbox
{
    /// <summary>
    /// Marca come presi dal <paramref name="worker"/> fino a <paramref name="scadenza"/> i messaggi pendenti già pronti (al più
    /// <paramref name="dimensioneLotto"/>, i più vecchi per primi) e ne restituisce gli identificativi.
    /// </summary>
    Task<IReadOnlyList<Guid>> PrenotaAsync(ContestoDatabase contesto, int dimensioneLotto, DateTime adesso, DateTime scadenza, Guid worker, CancellationToken annullamento);
}

using DvCms.Application.Piattaforma;
using DvCms.Models.Outbox;

namespace DvCms.Application.Outbox;

/// <summary>Contatori della coda: servono a vedere a colpo d'occhio se qualcosa non sta passando.</summary>
public sealed record RiepilogoCoda(int Pendenti, int Falliti, int Completati, DateTime? PiuVecchioPendenteIlUtc);

/// <summary>Un messaggio della coda, senza il contenuto (può contenere dati riservati, anche cifrati).</summary>
public sealed record MessaggioInCoda(
    Guid Id,
    Guid IdTenant,
    string NomeTenant,
    string Tipo,
    StatoMessaggioOutbox Stato,
    int Tentativi,
    DateTime CreatoIlUtc,
    DateTime ProssimoTentativoIlUtc,
    DateTime? ElaboratoIlUtc,
    string? UltimoErrore);

/// <summary>
/// Stato della coda dell'outbox per il <c>PlatformAdmin</c> (G-001): riepilogo, elenco dei messaggi con l'ultimo errore, ripresa
/// dei messaggi falliti e scarto. Ogni metodo riverifica sul database che l'attore sia un <c>PlatformAdmin</c> attivo; le letture
/// non autorizzate lanciano <see cref="UnauthorizedAccessException"/>, le scritture negate sono registrate nell'audit.
/// </summary>
public interface IGestioneCodaOutbox
{
    Task<RiepilogoCoda> RiepilogoAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>I messaggi più recenti nello stato indicato (tutti se nullo), fino a <paramref name="massimo"/>.</summary>
    Task<IReadOnlyList<MessaggioInCoda>> ElencaAsync(Guid idAttore, StatoMessaggioOutbox? stato, int massimo, CancellationToken annullamento);

    /// <summary>Rimette in coda un messaggio fallito (contatore dei tentativi azzerato, subito eseguibile). Solo i falliti.</summary>
    Task<EsitoOperazione> RitentaAsync(Guid idAttore, Guid idMessaggio, CancellationToken annullamento);

    /// <summary>Elimina un messaggio fallito che non serve più ritentare. Solo i falliti.</summary>
    Task<EsitoOperazione> ScartaAsync(Guid idAttore, Guid idMessaggio, CancellationToken annullamento);
}

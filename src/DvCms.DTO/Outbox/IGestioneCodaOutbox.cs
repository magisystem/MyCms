using DvCms.Models.Outbox;

namespace DvCms.DTO.Outbox;

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

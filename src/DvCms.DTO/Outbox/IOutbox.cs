namespace DvCms.DTO.Outbox;

/// <summary>Messaggio consegnato a un gestore.</summary>
public sealed record MessaggioDaElaborare(Guid Id, Guid IdTenant, string Tipo, string Payload, int Tentativo);

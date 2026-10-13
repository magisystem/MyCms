
namespace DvCms.DTO.Negozio;

/// <summary>Una variante con lo stato della sua giacenza. <c>Gestita</c> falso significa vendita senza limite.</summary>
public sealed record GiacenzaVista(Guid IdVariante, string Sku, string NomeProdotto, string? EtichettaVariante, bool Gestita, int Disponibile, int Riservata);

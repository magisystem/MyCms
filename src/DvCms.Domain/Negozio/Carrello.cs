namespace DvCms.Domain.Negozio;

/// <summary>
/// Carrello di un acquirente (F-002): uno per account e per sito (ordina solo chi è autenticato, ADR-0011). Contiene soltanto
/// identificativi di varianti e quantità: prezzi, IVA e totali non si conservano qui e si ricalcolano dal catalogo a ogni lettura.
/// </summary>
public class Carrello
{
    public const int RigheMassime = 50;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdUtenza { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

public class RigaCarrello
{
    public const int QuantitaMassima = 99;

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdCarrello { get; set; }

    public Guid IdVariante { get; set; }

    public int Quantita { get; set; }

    public DateTime AggiuntaIlUtc { get; set; }
}

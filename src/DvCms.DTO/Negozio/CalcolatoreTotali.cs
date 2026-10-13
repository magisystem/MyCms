using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

/// <summary>
/// Una riga calcolata. Il prezzo è **IVA inclusa** (ADR-0020): <see cref="Imponibile"/> e <see cref="Iva"/> sono lo scorporo del
/// <see cref="Lordo"/> e la loro somma è sempre uguale al lordo, al centesimo.
/// </summary>
public sealed record RigaCalcolata(decimal PrezzoUnitarioLordo, int Quantita, decimal Aliquota, decimal Lordo, decimal Imponibile, decimal Iva);

public sealed record TotaliCalcolati(IReadOnlyList<RigaCalcolata> Righe, decimal Lordo, decimal Imponibile, decimal Iva);

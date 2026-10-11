using System.Collections.Frozen;

namespace DvCms.Domain.Negozio;

public enum TipoMetodoSpedizione
{
    /// <summary>Consegna a un indirizzo, con tariffa fissa per l'area di destinazione.</summary>
    Zona = 1,

    /// <summary>Ritiro in sede: nessun indirizzo, nessun costo.</summary>
    Ritiro = 2,
}

/// <summary>Aree di destinazione delle spedizioni (ADR-0020): Italia e resto dell'Unione europea.</summary>
public enum AreaSpedizione
{
    Italia = 1,
    UnioneEuropea = 2,
}

public static class PaesiSpedizione
{
    private static readonly FrozenSet<string> _unioneEuropea = new[]
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Vero se il paese (codice ISO a due lettere maiuscole) appartiene all'area: <c>UnioneEuropea</c> esclude l'Italia, che è un'area a sé.</summary>
    public static bool Appartiene(AreaSpedizione area, string? paese) => area switch
    {
        AreaSpedizione.Italia => string.Equals(paese, "IT", StringComparison.Ordinal),
        AreaSpedizione.UnioneEuropea => paese is not null && _unioneEuropea.Contains(paese),
        _ => false,
    };

    public static IReadOnlyCollection<string> Elenco(AreaSpedizione area) =>
        area == AreaSpedizione.Italia ? ["IT"] : [.. _unioneEuropea.Order(StringComparer.Ordinal)];
}

/// <summary>
/// Modo di consegna offerto da un sito (F-002/F-003, ADR-0020): tariffa fissa per area o ritiro in sede. La tariffa è **IVA inclusa**,
/// in EUR, con l'aliquota ordinaria. Gli ordini ne conservano uno snapshot e non puntano a questa riga.
/// </summary>
public class MetodoSpedizione
{
    public const int LunghezzaMassimaNome = 100;
    public const decimal TariffaMassima = 10_000m;
    public const int NumeroMassimo = 20;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public TipoMetodoSpedizione Tipo { get; set; }

    /// <summary>Area servita; nulla per il ritiro in sede.</summary>
    public AreaSpedizione? Area { get; set; }

    public string Nome { get; set; } = string.Empty;

    /// <summary>Tariffa IVA inclusa: zero per il ritiro (e ammessa per le spedizioni gratuite).</summary>
    public decimal TariffaLorda { get; set; }

    public bool Attivo { get; set; } = true;

    public int Posizione { get; set; }

    public static bool TariffaValida(decimal tariffa) => tariffa >= 0 && tariffa <= TariffaMassima && decimal.Round(tariffa, 2) == tariffa;
}

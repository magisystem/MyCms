using DvCms.Application.Temi;

namespace DvCms.Web.Pubblico;

/// <summary>
/// Associa ogni versione di tema alla sua vista e al suo foglio di stile (ADR-0006). Le chiavi sono quelle del codice: il
/// tenant sceglie un tema dal registro ma non fornisce mai un percorso. Una versione sconosciuta (non dovrebbe succedere)
/// ripiega sul tema predefinito.
/// </summary>
public static class TemiVista
{
    private static readonly Dictionary<(string Chiave, int Versione), (string Vista, string Foglio)> _temi = new()
    {
        [("classico", 1)] = ("Temi/_classico_v1", "~/css/temi/classico-v1.css"),
        [("moderno", 1)] = ("Temi/_moderno_v1", "~/css/temi/moderno-v1.css"),
    };

    public static string Vista(TemaRisolto? tema) => Voce(tema).Vista;

    public static string Foglio(TemaRisolto? tema) => Voce(tema).Foglio;

    /// <summary>Classe CSS del corpo del documento, dalla chiave del tema in vigore.</summary>
    public static string Classe(TemaRisolto? tema) => $"tema-{ChiaveEffettiva(tema)}";

    private static string ChiaveEffettiva(TemaRisolto? tema) =>
        tema is not null && _temi.ContainsKey((tema.Chiave, tema.Versione)) ? tema.Chiave : RegistroTemi.ChiavePredefinita;

    private static (string Vista, string Foglio) Voce(TemaRisolto? tema) =>
        tema is not null && _temi.TryGetValue((tema.Chiave, tema.Versione), out var voce) ? voce : _temi[(RegistroTemi.ChiavePredefinita, 1)];
}

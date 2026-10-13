
namespace DvCms.DTO.Temi;

/// <summary>Tema in vigore per il sito dell'host, pronto per disegnare le pagine pubbliche e le anteprime.</summary>
public sealed record TemaRisolto(
    string Chiave,
    int Versione,
    string NomeSito,
    IReadOnlyDictionary<string, string> Valori,
    string VariabiliCss);

/// <summary>Tema scelto e impostazioni salvate (non quelle in vigore) per l'editor, con i temi che si possono scegliere.</summary>
public sealed record ConfigurazioneTema(
    string Chiave,
    int Versione,
    IReadOnlyDictionary<string, string> Valori,
    string TokenConcorrenza,
    bool Predefinito,
    IReadOnlyList<DefinizioneTema> Disponibili);

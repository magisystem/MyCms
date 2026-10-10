using DvCms.Application.Piattaforma;

namespace DvCms.Application.Temi;

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

/// <summary>
/// Gestione del tema del sito dell'host (D-001). L'attore deve essere un <c>TenantAdmin</c> di quel tenant o un
/// <c>PlatformAdmin</c>, verificato sul database a ogni operazione. Si sceglie solo fra i temi del registro e le impostazioni
/// sono validate contro lo schema del tema: il tenant non fornisce mai percorsi di vista, assembly o fogli di stile.
/// </summary>
public interface IServizioTemi
{
    Task<ConfigurazioneTema> LeggiAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>
    /// Salva il tema e le sue impostazioni. Scegliendo un tema diverso da quello attuale si usa la sua ultima versione; restando
    /// sullo stesso tema si resta sulla versione già configurata (un tema nuovo non cambia da solo i siti esistenti).
    /// Il token è quello letto con la configurazione (vuoto se il sito usa ancora il tema predefinito).
    /// </summary>
    Task<EsitoOperazione> SalvaAsync(
        Guid idAttore,
        string? chiave,
        IReadOnlyDictionary<string, string>? valori,
        string? tokenConcorrenza,
        CancellationToken annullamento);
}

/// <summary>Risoluzione del tema per le pagine pubbliche e le anteprime (nessun attore: il tenant viene dall'host).</summary>
public interface IRisolutoreTema
{
    Task<TemaRisolto> RisolviAsync(CancellationToken annullamento);
}

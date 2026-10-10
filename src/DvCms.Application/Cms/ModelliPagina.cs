using System.Security.Cryptography;
using System.Text;

namespace DvCms.Application.Cms;

public enum TipoCampoModello
{
    /// <summary>Una riga di testo.</summary>
    TestoBreve = 1,

    /// <summary>Più righe di testo.</summary>
    TestoLungo = 2,

    /// <summary>Un indirizzo: percorso del sito (<c>/pagina</c>) oppure indirizzo <c>https://</c> assoluto.</summary>
    Url = 3,
}

/// <summary>Un campo di un modello di pagina. Le etichette localizzate stanno nella vista, non qui.</summary>
public sealed record CampoModello(string Nome, TipoCampoModello Tipo, bool Obbligatorio, int LunghezzaMassima);

/// <summary>
/// Cosa può modificare chi scrive la pagina con questo modello (policy di editing, applicata dal server a ogni salvataggio,
/// qualunque cosa invii il client).
/// </summary>
public sealed record PoliticaEditing(bool ContenutoLiberoAmmesso, int LunghezzaMassimaContenuto);

/// <summary>
/// Un modello di pagina in una versione precisa. Una versione è **immutabile**: per cambiarne i campi o le regole si
/// crea una nuova versione, e le revisioni già scritte continuano a usare (e a disegnarsi con) quella con cui sono nate.
/// </summary>
public sealed record ModelloPagina(
    string Id,
    int Versione,
    IReadOnlyList<CampoModello> Campi,
    PoliticaEditing Politica,
    IReadOnlyList<IReadOnlyList<string>> CampiTuttiONessuno,
    bool Builder = false)
{
    /// <summary>Impronta (SHA-256 esadecimale) di tutto ciò che definisce la versione: un test la fissa per ogni versione rilasciata.</summary>
    public string Impronta
    {
        get
        {
            var testo = new StringBuilder()
                .Append(Id).Append('|').Append(Versione).Append('|')
                .Append(Politica.ContenutoLiberoAmmesso).Append(':').Append(Politica.LunghezzaMassimaContenuto).Append('|');
            foreach (var campo in Campi)
            {
                testo.Append(campo.Nome).Append(':').Append(campo.Tipo).Append(':').Append(campo.Obbligatorio).Append(':').Append(campo.LunghezzaMassima).Append(';');
            }

            testo.Append('|');
            foreach (var gruppo in CampiTuttiONessuno)
            {
                testo.Append(string.Join('+', gruppo)).Append(';');
            }

            if (Builder)
            {
                // I modelli che usano il builder (E-004) hanno un'impronta propria; quella dei modelli a campi non cambia.
                testo.Append("|builder");
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo.ToString())));
        }
    }
}

/// <summary>
/// Registro dei modelli di pagina (C-006). Contiene tutte le versioni rilasciate; le nuove pagine usano l'ultima di
/// ciascun modello. Una versione non si modifica né si rimuove: il registro rifiuta due definizioni con lo stesso id e versione.
/// </summary>
public sealed class RegistroModelliPagina
{
    public const int LunghezzaMassimaUrl = 500;

    private readonly Dictionary<(string Id, int Versione), ModelloPagina> _modelli;

    public RegistroModelliPagina(IEnumerable<ModelloPagina> modelli)
    {
        ArgumentNullException.ThrowIfNull(modelli);

        _modelli = [];
        foreach (var modello in modelli)
        {
            if (!_modelli.TryAdd((modello.Id, modello.Versione), modello))
            {
                throw new ArgumentException($"Modello '{modello.Id}' versione {modello.Versione} definito due volte.", nameof(modelli));
            }
        }

        if (!_modelli.ContainsKey((ModelloTestoSemplice.Id, 1)))
        {
            throw new ArgumentException("Il modello predefinito 'testo-semplice' versione 1 è obbligatorio.", nameof(modelli));
        }
    }

    public static ModelloPagina ModelloTestoSemplice { get; } = new(
        "testo-semplice",
        1,
        [],
        new PoliticaEditing(ContenutoLiberoAmmesso: true, LunghezzaMassimaContenuto: 100_000),
        []);

    public static ModelloPagina ModelloPresentazione { get; } = new(
        "presentazione",
        1,
        [
            new CampoModello("Sottotitolo", TipoCampoModello.TestoBreve, Obbligatorio: false, LunghezzaMassima: 200),
            new CampoModello("EtichettaPulsante", TipoCampoModello.TestoBreve, Obbligatorio: false, LunghezzaMassima: 40),
            new CampoModello("UrlPulsante", TipoCampoModello.Url, Obbligatorio: false, LunghezzaMassima: LunghezzaMassimaUrl),
        ],
        new PoliticaEditing(ContenutoLiberoAmmesso: true, LunghezzaMassimaContenuto: 20_000),
        [["EtichettaPulsante", "UrlPulsante"]]);

    /// <summary>
    /// Modelli che si scrivono con il page builder (E-004): nessun campo e nessun testo libero; il contenuto è il documento del
    /// builder, vincolato dalla struttura e dalla policy del template omonimo (<see cref="Builder.RegistroTemplateBuilder"/>).
    /// </summary>
    public static ModelloPagina ModelloPaginaLibera { get; } = ModelloBuilder(Builder.RegistroTemplateBuilder.PaginaLibera, 1);

    public static ModelloPagina ModelloIntestazioneFissa { get; } = ModelloBuilder(Builder.RegistroTemplateBuilder.IntestazioneFissa, 1);

    public static ModelloPagina ModelloIntestazioneFissaV2 { get; } = ModelloBuilder(Builder.RegistroTemplateBuilder.IntestazioneFissa, 2);

    /// <summary>(Dichiarata dopo i modelli: l'ordine degli inizializzatori statici conta.) I modelli rilasciati con il prodotto.</summary>
    public static IReadOnlyList<ModelloPagina> Predefiniti { get; } = [ModelloTestoSemplice, ModelloPresentazione, ModelloPaginaLibera, ModelloIntestazioneFissa, ModelloIntestazioneFissaV2];

    private static ModelloPagina ModelloBuilder(string id, int versione = 1) =>
        new(id, versione, [], new PoliticaEditing(ContenutoLiberoAmmesso: false, LunghezzaMassimaContenuto: 0), [], Builder: true);

    /// <summary>Tutte le versioni, in ordine di id e versione.</summary>
    public IReadOnlyList<ModelloPagina> Tutti => [.. _modelli.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ThenBy(m => m.Versione)];

    /// <summary>L'ultima versione di ogni modello (quella proposta per le nuove pagine).</summary>
    public IReadOnlyList<ModelloPagina> Ultimi => [.. Tutti.GroupBy(m => m.Id).Select(gruppo => gruppo.MaxBy(m => m.Versione)!)];

    public ModelloPagina? Trova(string? id, int versione) =>
        id is not null && _modelli.TryGetValue((id, versione), out var modello) ? modello : null;

    public ModelloPagina? Ultimo(string? id) =>
        id is null ? null : _modelli.Values.Where(m => m.Id == id).MaxBy(m => m.Versione);

    /// <summary>
    /// Controlla i valori di un modello e del contenuto libero. Restituisce i nomi dei campi non validi (mai i valori):
    /// vuota se tutto è a posto. Il modello e la versione devono esistere; i campi non previsti sono rifiutati.
    /// </summary>
    public IReadOnlyList<string> Valida(string? id, int versione, IReadOnlyDictionary<string, string>? dati, string? contenuto)
    {
        var modello = Trova(id, versione);
        if (modello is null)
        {
            return ["Modello"];
        }

        dati ??= new Dictionary<string, string>();
        var errori = new List<string>();
        foreach (var nome in dati.Keys.Where(chiave => modello.Campi.All(campo => campo.Nome != chiave)))
        {
            errori.Add(nome.Length > 50 ? nome[..50] : nome);
        }

        foreach (var campo in modello.Campi)
        {
            var valore = dati.TryGetValue(campo.Nome, out var grezzo) ? grezzo.Trim() : string.Empty;
            if (valore.Length == 0)
            {
                if (campo.Obbligatorio)
                {
                    errori.Add(campo.Nome);
                }

                continue;
            }

            if (valore.Length > campo.LunghezzaMassima || !TestoValido(campo.Tipo, valore))
            {
                errori.Add(campo.Nome);
            }
        }

        foreach (var gruppo in modello.CampiTuttiONessuno)
        {
            var compilati = gruppo.Count(nome => dati.TryGetValue(nome, out var v) && !string.IsNullOrWhiteSpace(v));
            if (compilati != 0 && compilati != gruppo.Count)
            {
                errori.AddRange(gruppo.Where(nome => !(dati.TryGetValue(nome, out var v) && !string.IsNullOrWhiteSpace(v))));
            }
        }

        var testoLibero = contenuto?.Trim() ?? string.Empty;
        if ((!modello.Politica.ContenutoLiberoAmmesso && testoLibero.Length > 0) || testoLibero.Length > modello.Politica.LunghezzaMassimaContenuto)
        {
            errori.Add("Contenuto");
        }

        return [.. errori.Distinct()];
    }

    /// <summary>Valori ripuliti (spazi tolti, campi vuoti omessi) nella forma che si salva.</summary>
    public static IReadOnlyDictionary<string, string> Normalizza(IReadOnlyDictionary<string, string>? dati) =>
        (dati ?? new Dictionary<string, string>())
            .Select(coppia => (coppia.Key, Valore: coppia.Value.Trim()))
            .Where(coppia => coppia.Valore.Length > 0)
            .ToDictionary(coppia => coppia.Key, coppia => coppia.Valore, StringComparer.Ordinal);

    private static bool TestoValido(TipoCampoModello tipo, string valore) => tipo switch
    {
        TipoCampoModello.TestoBreve => !valore.Any(char.IsControl),
        TipoCampoModello.TestoLungo => !valore.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')),
        TipoCampoModello.Url => UrlConsentito(valore),
        _ => false,
    };

    /// <summary>Solo percorsi del sito (<c>/x</c>, non <c>//host</c>) o indirizzi <c>https://</c> assoluti: mai <c>javascript:</c>, <c>data:</c> e simili.</summary>
    public static bool UrlConsentito(string valore)
    {
        if (valore.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || valore.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (valore.StartsWith('/'))
        {
            return !valore.StartsWith("//", StringComparison.Ordinal);
        }

        return Uri.TryCreate(valore, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && !string.IsNullOrEmpty(uri.Host);
    }
}

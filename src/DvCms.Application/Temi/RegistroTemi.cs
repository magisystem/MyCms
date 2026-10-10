using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DvCms.Application.Temi;

public enum TipoCampoTema
{
    /// <summary>Un colore esadecimale <c>#RRGGBB</c>.</summary>
    Colore = 1,

    /// <summary>Una riga di testo breve (sempre codificata in uscita).</summary>
    Testo = 2,

    /// <summary>Uno fra i valori ammessi dallo schema.</summary>
    Scelta = 3,
}

/// <summary>Un'impostazione di un tema. Le etichette localizzate stanno nella vista, non qui.</summary>
public sealed record CampoTema(string Nome, TipoCampoTema Tipo, string Predefinito, int LunghezzaMassima = 0, IReadOnlyList<string>? Valori = null);

/// <summary>
/// Un tema in una versione precisa (ADR-0006): presentazione, schema delle impostazioni, e nient'altro. La vista e il foglio di
/// stile del tema sono nel codice, scelti dalla chiave e dalla versione; il tenant non fornisce mai percorsi. Una versione è
/// **immutabile**: per cambiarla si aggiunge una versione nuova, e i siti restano su quella con cui sono configurati.
/// </summary>
public sealed record DefinizioneTema(string Chiave, int Versione, IReadOnlyList<CampoTema> Campi)
{
    /// <summary>Impronta (SHA-256 esadecimale) di ciò che definisce la versione: un test la fissa per ogni versione rilasciata.</summary>
    public string Impronta
    {
        get
        {
            var testo = new StringBuilder().Append(Chiave).Append('|').Append(Versione).Append('|');
            foreach (var campo in Campi)
            {
                testo.Append(campo.Nome).Append(':').Append(campo.Tipo).Append(':').Append(campo.Predefinito).Append(':').Append(campo.LunghezzaMassima).Append(':')
                    .Append(string.Join(',', campo.Valori ?? [])).Append(';');
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo.ToString())));
        }
    }
}

/// <summary>
/// Registro dei temi (allow-list, D-001). Contiene tutte le versioni rilasciate; un sito sceglie fra queste e il registro è
/// l'unico modo per risolvere un tema: chiavi sconosciute non vengono mai interpretate come percorsi o tipi.
/// </summary>
public sealed partial class RegistroTemi
{
    public const string ChiavePredefinita = "classico";

    private readonly Dictionary<(string Chiave, int Versione), DefinizioneTema> _temi;

    public RegistroTemi(IEnumerable<DefinizioneTema> temi)
    {
        ArgumentNullException.ThrowIfNull(temi);

        _temi = [];
        foreach (var tema in temi)
        {
            if (!ChiaveValida().IsMatch(tema.Chiave) || tema.Versione < 1)
            {
                throw new ArgumentException($"Tema '{tema.Chiave}' non valido.", nameof(temi));
            }

            if (tema.Campi.GroupBy(campo => campo.Nome).Any(gruppo => gruppo.Count() > 1) || tema.Campi.Any(campo => !CampoValido(campo)))
            {
                throw new ArgumentException($"Schema del tema '{tema.Chiave}' non valido.", nameof(temi));
            }

            if (!_temi.TryAdd((tema.Chiave, tema.Versione), tema))
            {
                throw new ArgumentException($"Tema '{tema.Chiave}' versione {tema.Versione} definito due volte.", nameof(temi));
            }
        }

        if (!_temi.ContainsKey((ChiavePredefinita, 1)))
        {
            throw new ArgumentException($"Il tema predefinito '{ChiavePredefinita}' versione 1 è obbligatorio.", nameof(temi));
        }
    }

    public static DefinizioneTema TemaClassico { get; } = new(
        ChiavePredefinita,
        1,
        [
            new CampoTema("ColorePrimario", TipoCampoTema.Colore, "#1f4e8c"),
            new CampoTema("TestoPiePagina", TipoCampoTema.Testo, string.Empty, LunghezzaMassima: 200),
        ]);

    public static DefinizioneTema TemaModerno { get; } = new(
        "moderno",
        1,
        [
            new CampoTema("ColorePrimario", TipoCampoTema.Colore, "#0f766e"),
            new CampoTema("ColoreIntestazione", TipoCampoTema.Colore, "#111827"),
            new CampoTema("Larghezza", TipoCampoTema.Scelta, "Stretta", Valori: ["Stretta", "Larga"]),
            new CampoTema("TestoPiePagina", TipoCampoTema.Testo, string.Empty, LunghezzaMassima: 200),
        ]);

    /// <summary>(Dichiarata dopo i temi: l'ordine degli inizializzatori statici conta.) I temi rilasciati con il prodotto.</summary>
    public static IReadOnlyList<DefinizioneTema> Predefiniti { get; } = [TemaClassico, TemaModerno];

    public IReadOnlyList<DefinizioneTema> Tutti => [.. _temi.Values.OrderBy(t => t.Chiave, StringComparer.Ordinal).ThenBy(t => t.Versione)];

    /// <summary>L'ultima versione di ogni tema (quella proposta quando si sceglie un tema).</summary>
    public IReadOnlyList<DefinizioneTema> Ultimi => [.. Tutti.GroupBy(t => t.Chiave).Select(gruppo => gruppo.MaxBy(t => t.Versione)!)];

    public DefinizioneTema Predefinito => Ultimo(ChiavePredefinita)!;

    public DefinizioneTema? Trova(string? chiave, int versione) =>
        chiave is not null && _temi.TryGetValue((chiave, versione), out var tema) ? tema : null;

    public DefinizioneTema? Ultimo(string? chiave) =>
        chiave is null ? null : _temi.Values.Where(t => t.Chiave == chiave).MaxBy(t => t.Versione);

    /// <summary>
    /// Controlla i valori rispetto allo schema del tema. Restituisce i nomi dei campi non validi (mai i valori), vuota se
    /// tutto è a posto. Il tema e la versione devono esistere; i campi non previsti sono rifiutati; un campo vuoto vale "predefinito".
    /// </summary>
    public IReadOnlyList<string> Valida(string? chiave, int versione, IReadOnlyDictionary<string, string>? valori)
    {
        var tema = Trova(chiave, versione);
        if (tema is null)
        {
            return ["Tema"];
        }

        valori ??= new Dictionary<string, string>();
        var errori = new List<string>();
        foreach (var nome in valori.Keys.Where(chiaveCampo => tema.Campi.All(campo => campo.Nome != chiaveCampo)))
        {
            errori.Add(nome.Length > 50 ? nome[..50] : nome);
        }

        foreach (var campo in tema.Campi)
        {
            var valore = valori.TryGetValue(campo.Nome, out var grezzo) ? grezzo.Trim() : string.Empty;
            if (valore.Length > 0 && !ValoreValido(campo, valore))
            {
                errori.Add(campo.Nome);
            }
        }

        return errori;
    }

    /// <summary>Valori ripuliti nella forma che si salva: spazi tolti, vuoti e uguali al predefinito omessi, colori in minuscolo.</summary>
    public IReadOnlyDictionary<string, string> Normalizza(string chiave, int versione, IReadOnlyDictionary<string, string>? valori)
    {
        var tema = Trova(chiave, versione) ?? throw new ArgumentException("Tema sconosciuto.", nameof(chiave));
        var risultato = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var campo in tema.Campi)
        {
            var valore = valori is not null && valori.TryGetValue(campo.Nome, out var grezzo) ? grezzo.Trim() : string.Empty;
            if (campo.Tipo == TipoCampoTema.Colore)
            {
                valore = valore.ToLowerInvariant();
            }

            if (valore.Length > 0 && valore != campo.Predefinito)
            {
                risultato[campo.Nome] = valore;
            }
        }

        return risultato;
    }

    /// <summary>
    /// Valori in vigore: i predefiniti del tema con sopra quelli salvati che sono ancora validi. Un valore salvato non più valido
    /// (non dovrebbe succedere) si ignora in silenzio: il sito pubblico non si rompe per un'impostazione.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Effettivi(DefinizioneTema tema, IReadOnlyDictionary<string, string>? salvati)
    {
        ArgumentNullException.ThrowIfNull(tema);

        var risultato = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var campo in tema.Campi)
        {
            var valore = salvati is not null && salvati.TryGetValue(campo.Nome, out var grezzo) ? grezzo.Trim() : string.Empty;
            risultato[campo.Nome] = valore.Length > 0 && ValoreValido(campo, valore) ? valore : campo.Predefinito;
        }

        return risultato;
    }

    /// <summary>Variabili CSS dei soli campi colore (già validati), nella forma <c>--tema-nome-campo:#rrggbb;</c>.</summary>
    public static string VariabiliCss(DefinizioneTema tema, IReadOnlyDictionary<string, string> effettivi)
    {
        ArgumentNullException.ThrowIfNull(tema);
        ArgumentNullException.ThrowIfNull(effettivi);

        var css = new StringBuilder();
        foreach (var campo in tema.Campi.Where(c => c.Tipo == TipoCampoTema.Colore))
        {
            if (effettivi.TryGetValue(campo.Nome, out var colore) && ColoreEsadecimale().IsMatch(colore))
            {
                css.Append("--tema-").Append(NomeCss(campo.Nome)).Append(':').Append(colore.ToLowerInvariant()).Append(';');
            }
        }

        return css.ToString();
    }

    private static string NomeCss(string nome) =>
        string.Concat(nome.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "-" : string.Empty) + char.ToLower(c, CultureInfo.InvariantCulture) : c.ToString()));

    private static bool CampoValido(CampoTema campo) =>
        NomeCampoValido().IsMatch(campo.Nome)
        && ValoreValido(campo, campo.Predefinito, ammettiVuoto: true)
        && (campo.Tipo != TipoCampoTema.Scelta || campo.Valori is { Count: > 0 })
        && (campo.Tipo != TipoCampoTema.Testo || campo.LunghezzaMassima > 0);

    private static bool ValoreValido(CampoTema campo, string valore, bool ammettiVuoto = false)
    {
        if (valore.Length == 0)
        {
            return ammettiVuoto && campo.Tipo != TipoCampoTema.Colore;
        }

        return campo.Tipo switch
        {
            TipoCampoTema.Colore => ColoreEsadecimale().IsMatch(valore),
            TipoCampoTema.Testo => valore.Length <= campo.LunghezzaMassima && !valore.Any(char.IsControl),
            TipoCampoTema.Scelta => campo.Valori?.Contains(valore) == true,
            _ => false,
        };
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChiaveValida();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex NomeCampoValido();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColoreEsadecimale();
}

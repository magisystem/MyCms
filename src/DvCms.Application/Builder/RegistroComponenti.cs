using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DvCms.Application.Cms;

namespace DvCms.Application.Builder;

public enum TipoProprieta
{
    TestoBreve = 1,
    TestoLungo = 2,
    Url = 3,
    Enumerazione = 4,

    /// <summary>Identificativo (GUID) di una risorsa della libreria media; l'esistenza nel sito la verifica il servizio.</summary>
    IdRisorsa = 5,

    /// <summary>"si" oppure "no".</summary>
    Booleano = 6,
}

/// <summary>Una proprietà (o uno stile) ammessa da un componente. Gli stili sono sempre enumerazioni: token e varianti, mai CSS libero.</summary>
public sealed record DefinizioneProprieta(
    string Nome,
    TipoProprieta Tipo,
    bool Obbligatoria = false,
    int LunghezzaMassima = 0,
    IReadOnlyList<string>? Valori = null,
    string Predefinito = "");

/// <summary>Un componente del catalogo: ciò che accetta, cosa può contenere e cosa può fare l'editor con esso.</summary>
public sealed record DefinizioneComponente(
    string Tipo,
    int VersioneSchema,
    IReadOnlyList<DefinizioneProprieta> Proprieta,
    IReadOnlyList<DefinizioneProprieta> Stili,
    IReadOnlyList<string> FigliAmmessi,
    int FigliMinimi = 0,
    int FigliMassimi = 0)
{
    public bool Contenitore => FigliAmmessi.Count > 0;
}

/// <summary>
/// Registro dei componenti (allow-list, ADR-0005): l'unico elenco di tipi che il documento può contenere e che il renderer disegna.
/// Le definizioni sono nel codice e hanno un'impronta che un test fissa: cambiarle richiede una nuova versione di schema.
/// Valida il documento: identificativi, tipi, proprietà, stili, annidamento, cardinalità e limiti di profondità e dimensione.
/// </summary>
public sealed partial class RegistroComponenti
{
    public const int ProfonditaMassima = 6;
    public const int NodiMassimi = 300;
    public const int ByteMassimiDocumento = 262_144;
    public const int ErroriMassimi = 50;
    public const int LunghezzaMassimaId = 64;

    private static readonly string[] _contenutoBase = ["contenuto.titolo", "contenuto.testo", "contenuto.immagine", "contenuto.pulsante"];

    private static readonly IReadOnlyList<DefinizioneProprieta> _allineamento =
    [
        new("allineamento", TipoProprieta.Enumerazione, Valori: ["sinistra", "centro", "destra"], Predefinito: "sinistra"),
    ];

    private readonly Dictionary<string, DefinizioneComponente> _componenti;

    public RegistroComponenti(IEnumerable<DefinizioneComponente> componenti)
    {
        ArgumentNullException.ThrowIfNull(componenti);

        _componenti = [];
        foreach (var componente in componenti)
        {
            if (!_componenti.TryAdd(componente.Tipo, componente))
            {
                throw new ArgumentException($"Componente '{componente.Tipo}' definito due volte.", nameof(componenti));
            }
        }

        foreach (var figlio in _componenti.Values.SelectMany(c => c.FigliAmmessi).Where(tipo => !_componenti.ContainsKey(tipo)))
        {
            throw new ArgumentException($"Tipo figlio '{figlio}' non definito.", nameof(componenti));
        }
    }

    /// <summary>I componenti rilasciati con il prodotto (catalogo del proof of concept: layout e contenuti di base).</summary>
    public static IReadOnlyList<DefinizioneComponente> Predefiniti { get; } =
    [
        new("layout.sezione", 1, [],
            [
                new("spaziatura", TipoProprieta.Enumerazione, Valori: ["compatta", "normale", "ampia"], Predefinito: "normale"),
                new("sfondo", TipoProprieta.Enumerazione, Valori: ["nessuno", "chiaro", "scuro"], Predefinito: "nessuno"),
            ],
            ["layout.colonne", .. _contenutoBase], FigliMassimi: 50),
        new("layout.colonne", 1, [], [], ["layout.colonna"], FigliMinimi: 2, FigliMassimi: 3),
        new("layout.colonna", 1, [], [], _contenutoBase, FigliMassimi: 20),
        new("contenuto.titolo", 1,
            [
                new("testo", TipoProprieta.TestoBreve, Obbligatoria: true, LunghezzaMassima: 200),
                new("livello", TipoProprieta.Enumerazione, Valori: ["1", "2", "3"], Predefinito: "2"),
            ],
            _allineamento, []),
        new("contenuto.testo", 1,
            [new("testo", TipoProprieta.TestoLungo, Obbligatoria: true, LunghezzaMassima: 5000)],
            _allineamento, []),
        new("contenuto.immagine", 1,
            [
                new("idRisorsa", TipoProprieta.IdRisorsa, Obbligatoria: true),
                new("alt", TipoProprieta.TestoBreve, LunghezzaMassima: 200),
                new("decorativa", TipoProprieta.Booleano, Predefinito: "no"),
                new("didascalia", TipoProprieta.TestoBreve, LunghezzaMassima: 200),
            ],
            [], []),
        new("contenuto.pulsante", 1,
            [
                new("etichetta", TipoProprieta.TestoBreve, Obbligatoria: true, LunghezzaMassima: 40),
                new("url", TipoProprieta.Url, Obbligatoria: true, LunghezzaMassima: RegistroModelliPagina.LunghezzaMassimaUrl),
            ],
            [new("variante", TipoProprieta.Enumerazione, Valori: ["primario", "secondario"], Predefinito: "primario")],
            []),
    ];

    public IReadOnlyList<DefinizioneComponente> Tutti => [.. _componenti.Values.OrderBy(c => c.Tipo, StringComparer.Ordinal)];

    /// <summary>Impronta (SHA-256 esadecimale) di tutte le definizioni: un test la fissa, perché cambiarle cambia cosa significano i documenti salvati.</summary>
    public string Impronta
    {
        get
        {
            var testo = new StringBuilder();
            foreach (var c in Tutti)
            {
                testo.Append(c.Tipo).Append('|').Append(c.VersioneSchema).Append('|').Append(c.FigliMinimi).Append('|').Append(c.FigliMassimi).Append('|')
                    .Append(string.Join(',', c.FigliAmmessi)).Append('|');
                Aggiungi(testo, c.Proprieta);
                testo.Append('|');
                Aggiungi(testo, c.Stili);
                testo.Append('\n');
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo.ToString())));
        }
    }

    public DefinizioneComponente? Trova(string? tipo) => tipo is not null && _componenti.TryGetValue(tipo, out var componente) ? componente : null;

    /// <summary>
    /// Controlla il documento e restituisce i problemi (vuota se è valido). Non si ferma al primo errore, ma si limita a
    /// <see cref="ErroriMassimi"/>. I valori non compaiono mai negli errori.
    /// </summary>
    public IReadOnlyList<ErroreBuilder> Valida(DocumentoCms documento)
    {
        ArgumentNullException.ThrowIfNull(documento);

        var errori = new List<ErroreBuilder>();
        if (documento.VersioneSchema != DocumentoCms.VersioneCorrente)
        {
            errori.Add(new ErroreBuilder("VersioneSchemaNonSupportata"));
            return errori;
        }

        if (SerializzatoreDocumento.Scrivi(documento).Length > ByteMassimiDocumento)
        {
            errori.Add(new ErroreBuilder("DocumentoTroppoGrande"));
            return errori;
        }

        var identificativi = new HashSet<string>(StringComparer.Ordinal);
        var conteggio = 0;
        ValidaElenco(documento.Nodi, genitore: null, profondita: 1, identificativi, ref conteggio, errori);
        return errori.Count > ErroriMassimi ? [.. errori.Take(ErroriMassimi)] : errori;
    }

    /// <summary>Forma canonica: proprietà e stili uguali al default o vuoti sono omessi (stesso documento, stessa stringa).</summary>
    public DocumentoCms Normalizza(DocumentoCms documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        return documento with { Nodi = [.. documento.Nodi.Select(Normalizza)] };
    }

    private NodoCms Normalizza(NodoCms nodo)
    {
        var definizione = Trova(nodo.Tipo);
        return nodo with
        {
            Proprieta = Ripulisci(nodo.Proprieta, definizione?.Proprieta),
            Stili = Ripulisci(nodo.Stili, definizione?.Stili),
            Figli = [.. nodo.Figli.Select(Normalizza)],
        };
    }

    private static Dictionary<string, string> Ripulisci(IReadOnlyDictionary<string, string> valori, IReadOnlyList<DefinizioneProprieta>? definizioni) =>
        valori
            .Where(coppia => coppia.Value.Length > 0
                && !(definizioni?.FirstOrDefault(d => d.Nome == coppia.Key) is { } d && d.Predefinito.Length > 0 && d.Predefinito == coppia.Value))
            .ToDictionary(coppia => coppia.Key, coppia => coppia.Value, StringComparer.Ordinal);

    private void ValidaElenco(
        IReadOnlyList<NodoCms> nodi,
        DefinizioneComponente? genitore,
        int profondita,
        HashSet<string> identificativi,
        ref int conteggio,
        List<ErroreBuilder> errori)
    {
        foreach (var nodo in nodi)
        {
            if (errori.Count > ErroriMassimi)
            {
                return;
            }

            conteggio++;
            if (conteggio > NodiMassimi)
            {
                errori.Add(new ErroreBuilder("TroppiNodi"));
                return;
            }

            if (profondita > ProfonditaMassima)
            {
                errori.Add(new ErroreBuilder("ProfonditaSuperata", Visibile(nodo.Id)));
                continue;
            }

            if (!IdValido(nodo.Id))
            {
                errori.Add(new ErroreBuilder("IdNonValido", Visibile(nodo.Id)));
            }
            else if (!identificativi.Add(nodo.Id))
            {
                errori.Add(new ErroreBuilder("NodoDuplicato", nodo.Id));
            }

            var definizione = Trova(nodo.Tipo);
            if (definizione is null)
            {
                errori.Add(new ErroreBuilder("TipoSconosciuto", Visibile(nodo.Id)));
                continue;
            }

            if (genitore is not null && !genitore.FigliAmmessi.Contains(nodo.Tipo, StringComparer.Ordinal))
            {
                errori.Add(new ErroreBuilder("FiglioNonAmmesso", Visibile(nodo.Id)));
            }

            ValidaValori(nodo, nodo.Proprieta, definizione.Proprieta, "Proprieta", errori);
            ValidaValori(nodo, nodo.Stili, definizione.Stili, "Stile", errori);
            if (definizione.Tipo == "contenuto.immagine" && !AltValido(nodo))
            {
                errori.Add(new ErroreBuilder("AltMancante", nodo.Id, "alt"));
            }

            if (nodo.Figli.Count > 0 && !definizione.Contenitore)
            {
                errori.Add(new ErroreBuilder("FigliNonAmmessi", nodo.Id));
            }
            else if (definizione.Contenitore && (nodo.Figli.Count > definizione.FigliMassimi))
            {
                errori.Add(new ErroreBuilder("TroppiFigli", nodo.Id));
            }
            else if (definizione.Contenitore && nodo.Figli.Count < definizione.FigliMinimi)
            {
                errori.Add(new ErroreBuilder("FigliInsufficienti", nodo.Id));
            }

            ValidaElenco(nodo.Figli, definizione, profondita + 1, identificativi, ref conteggio, errori);
        }
    }

    private static void ValidaValori(
        NodoCms nodo,
        IReadOnlyDictionary<string, string> valori,
        IReadOnlyList<DefinizioneProprieta> definizioni,
        string prefisso,
        List<ErroreBuilder> errori)
    {
        foreach (var nome in valori.Keys.Where(chiave => definizioni.All(d => d.Nome != chiave)))
        {
            errori.Add(new ErroreBuilder($"{prefisso}Sconosciuta", Visibile(nodo.Id), Visibile(nome)));
        }

        foreach (var definizione in definizioni)
        {
            var valore = valori.TryGetValue(definizione.Nome, out var grezzo) ? grezzo : string.Empty;
            if (valore.Length == 0)
            {
                if (definizione.Obbligatoria)
                {
                    errori.Add(new ErroreBuilder($"{prefisso}Mancante", Visibile(nodo.Id), definizione.Nome));
                }

                continue;
            }

            if (!ValoreValido(definizione, valore))
            {
                errori.Add(new ErroreBuilder($"{prefisso}NonValida", Visibile(nodo.Id), definizione.Nome));
            }
        }
    }

    private static bool AltValido(NodoCms nodo)
    {
        var decorativa = nodo.Proprieta.TryGetValue("decorativa", out var d) && d == "si";
        return decorativa || (nodo.Proprieta.TryGetValue("alt", out var alt) && !string.IsNullOrWhiteSpace(alt));
    }

    private static bool ValoreValido(DefinizioneProprieta definizione, string valore) => definizione.Tipo switch
    {
        TipoProprieta.TestoBreve => valore.Length <= definizione.LunghezzaMassima && !valore.Any(char.IsControl),
        TipoProprieta.TestoLungo => valore.Length <= definizione.LunghezzaMassima && !valore.Any(c => char.IsControl(c) && c is not ('\n' or '\t')),
        TipoProprieta.Url => valore.Length <= definizione.LunghezzaMassima && RegistroModelliPagina.UrlConsentito(valore),
        TipoProprieta.Enumerazione => definizione.Valori?.Contains(valore, StringComparer.Ordinal) == true,
        TipoProprieta.IdRisorsa => Guid.TryParseExact(valore, "D", out var guid) && guid != Guid.Empty && guid.ToString("D") == valore,
        TipoProprieta.Booleano => valore is "si" or "no",
        _ => false,
    };

    public static bool IdValido(string? id) => id is { Length: > 0 and <= LunghezzaMassimaId } && FormaId().IsMatch(id);

    private static string? Visibile(string? testo) => testo is { Length: > LunghezzaMassimaId } ? testo[..LunghezzaMassimaId] : testo;

    private static void Aggiungi(StringBuilder testo, IReadOnlyList<DefinizioneProprieta> definizioni)
    {
        foreach (var d in definizioni)
        {
            testo.Append(d.Nome).Append(':').Append(d.Tipo).Append(':').Append(d.Obbligatoria).Append(':').Append(d.LunghezzaMassima).Append(':')
                .Append(d.Valori is null ? string.Empty : string.Join('/', d.Valori)).Append(':').Append(d.Predefinito).Append(';');
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaId();
}

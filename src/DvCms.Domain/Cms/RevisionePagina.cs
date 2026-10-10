namespace DvCms.Domain.Cms;

/// <summary>
/// Contenuto di una revisione: i campi di base (titolo e testo libero) e quelli del modello di pagina scelto.
/// La validazione dei campi del modello spetta al registro dei modelli (Application); qui si garantiscono solo
/// i vincoli di base e la forma dei dati.
/// </summary>
public sealed record ContenutoRevisione(
    string Titolo,
    string Contenuto,
    string IdModello = RevisionePagina.ModelloPredefinito,
    int VersioneModello = 1,
    IReadOnlyDictionary<string, string>? DatiModello = null,
    string? TitoloSeo = null,
    string? DescrizioneSeo = null,
    bool NonIndicizzare = false,
    DocumentoBuilder? Builder = null);

/// <summary>
/// Il documento del page builder di una revisione: il documento CMS canonico che il renderer disegna e il progetto per riaprire
/// l'editor (derivato dal documento, ADR-0005). Sono JSON già validati dall'Application: il dominio non li interpreta.
/// </summary>
public sealed record DocumentoBuilder(string ProgettoEditorJson, string DocumentoRenderJson)
{
    public const int ByteMassimi = 2_000_000;
}

/// <summary>
/// Versione immutabile del contenuto di una pagina. In M1 il contenuto è testo semplice (titolo e paragrafi);
/// il documento del builder e l'HTML ripulito arrivano con le milestone successive, aggiungendo colonne.
/// </summary>
public class RevisionePagina
{
    public const string ModelloPredefinito = "testo-semplice";
    public const int LunghezzaMassimaIdModello = 50;
    public const int LunghezzaMassimaTitoloSeo = 200;
    public const int LunghezzaMassimaDescrizioneSeo = 500;

    public const int LunghezzaMassimaTitolo = 200;
    public const int LunghezzaMassimaContenuto = 100_000;
    public const int LunghezzaMassimaNota = 500;
    public const int VersioneSchemaTestoSemplice = 1;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdPagina { get; set; }

    public int NumeroRevisione { get; set; }

    public int VersioneSchema { get; set; } = VersioneSchemaTestoSemplice;

    public string Titolo { get; set; } = string.Empty;

    /// <summary>Testo semplice: i paragrafi sono separati da una riga vuota. Non è HTML e viene sempre codificato in uscita.</summary>
    public string Contenuto { get; set; } = string.Empty;

    /// <summary>Modello di pagina e versione con cui la revisione è stata scritta (e va disegnata): non cambiano mai.</summary>
    public string IdModello { get; set; } = ModelloPredefinito;

    public int VersioneModello { get; set; } = 1;

    /// <summary>Valori dei campi del modello, come oggetto JSON nome-valore.</summary>
    public string DatiModelloJson { get; set; } = "{}";

    /// <summary>Titolo per i motori di ricerca; se vuoto si usa <see cref="Titolo"/>.</summary>
    public string? TitoloSeo { get; set; }

    public string? DescrizioneSeo { get; set; }

    /// <summary>Se vero la pagina chiede di non essere indicizzata (<c>noindex</c>).</summary>
    public bool NonIndicizzare { get; set; }

    /// <summary>
    /// Progetto per riaprire il builder (GrapesJS), rigenerato dal server a partire dal documento validato; nullo per le revisioni
    /// che non usano il builder.
    /// </summary>
    public string? ProgettoEditorJson { get; set; }

    /// <summary>Documento CMS canonico (JSON versionato) che il renderer disegna; nullo per le revisioni che non usano il builder.</summary>
    public string? DocumentoRenderJson { get; set; }

    /// <summary>Contenuto del builder di questa revisione, se c'è.</summary>
    public DocumentoBuilder? Builder => ProgettoEditorJson is not null && DocumentoRenderJson is not null
        ? new DocumentoBuilder(ProgettoEditorJson, DocumentoRenderJson)
        : null;

    /// <summary>
    /// Vera se la revisione è un salvataggio automatico del builder. Una revisione automatica non pubblicata e ancora in cima alla
    /// cronologia viene sostituita dal salvataggio successivo dello stesso autore (e da quello manuale): la cronologia non si riempie
    /// di salvataggi intermedi. Le revisioni manuali e quelle pubblicate non si toccano mai.
    /// </summary>
    public bool SalvataggioAutomatico { get; set; }

    public string? NotaRevisione { get; set; }

    public Guid CreatoDaIdUtenza { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public static bool TentaValidaTesto(string? titolo, string? contenuto, out string titoloValido, out string contenutoValido)
    {
        titoloValido = titolo?.Trim() ?? string.Empty;
        contenutoValido = contenuto?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() ?? string.Empty;
        return titoloValido.Length is > 0 and <= LunghezzaMassimaTitolo
            && !titoloValido.Any(char.IsControl)
            && contenutoValido.Length <= LunghezzaMassimaContenuto
            && !contenutoValido.Any(carattere => char.IsControl(carattere) && carattere != '\n' && carattere != '\t');
    }

    /// <summary>Valori dei campi del modello letti dal JSON salvato.</summary>
    public IReadOnlyDictionary<string, string> DatiModello =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(DatiModelloJson) ?? [];

    /// <summary>Normalizza i campi SEO (spazi tolti, vuoti → nulli) e ne controlla lunghezza e caratteri.</summary>
    public static bool TentaValidaSeo(string? titoloSeo, string? descrizioneSeo, out string? titolo, out string? descrizione)
    {
        titolo = string.IsNullOrWhiteSpace(titoloSeo) ? null : titoloSeo.Trim();
        descrizione = string.IsNullOrWhiteSpace(descrizioneSeo) ? null : descrizioneSeo.Trim();
        return (titolo is null || (titolo.Length <= LunghezzaMassimaTitoloSeo && !titolo.Any(char.IsControl)))
            && (descrizione is null || (descrizione.Length <= LunghezzaMassimaDescrizioneSeo && !descrizione.Any(char.IsControl)));
    }

    internal static RevisionePagina Crea(
        Pagina pagina,
        int numero,
        ContenutoRevisione dati,
        string? nota,
        Guid idAutore,
        DateTime adesso,
        TimeProvider orologio,
        bool automatica = false)
    {
        ArgumentNullException.ThrowIfNull(dati);
        if (!TentaValidaTesto(dati.Titolo, dati.Contenuto, out var titoloValido, out var contenutoValido))
        {
            throw new ArgumentException("Titolo o contenuto non validi.", nameof(dati));
        }

        if (!TentaValidaSeo(dati.TitoloSeo, dati.DescrizioneSeo, out var titoloSeo, out var descrizioneSeo))
        {
            throw new ArgumentException("Campi SEO non validi.", nameof(dati));
        }

        if (string.IsNullOrWhiteSpace(dati.IdModello) || dati.IdModello.Length > LunghezzaMassimaIdModello || dati.VersioneModello < 1)
        {
            throw new ArgumentException("Modello di pagina non valido.", nameof(dati));
        }

        if (dati.Builder is { } builder
            && (builder.ProgettoEditorJson.Length > DocumentoBuilder.ByteMassimi || builder.DocumentoRenderJson.Length > DocumentoBuilder.ByteMassimi))
        {
            throw new ArgumentException("Documento del builder troppo grande.", nameof(dati));
        }

        var notaValida = nota?.Trim();
        if (notaValida is { Length: > LunghezzaMassimaNota })
        {
            notaValida = notaValida[..LunghezzaMassimaNota];
        }

        return new RevisionePagina
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = pagina.IdTenant,
            IdSito = pagina.IdSito,
            IdPagina = pagina.Id,
            NumeroRevisione = numero,
            Titolo = titoloValido,
            Contenuto = contenutoValido,
            IdModello = dati.IdModello,
            VersioneModello = dati.VersioneModello,
            TitoloSeo = titoloSeo,
            DescrizioneSeo = descrizioneSeo,
            NonIndicizzare = dati.NonIndicizzare,
            ProgettoEditorJson = dati.Builder?.ProgettoEditorJson,
            DocumentoRenderJson = dati.Builder?.DocumentoRenderJson,
            DatiModelloJson = System.Text.Json.JsonSerializer.Serialize(dati.DatiModello ?? new Dictionary<string, string>()),
            NotaRevisione = string.IsNullOrEmpty(notaValida) ? null : notaValida,
            SalvataggioAutomatico = automatica,
            CreatoDaIdUtenza = idAutore,
            CreatoIlUtc = adesso,
        };
    }
}

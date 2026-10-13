using System.Text;
using System.Text.Json;
using DvCms.DTO.Builder;

namespace DvCms.Core.Builder;

/// <summary>
/// Adapter tra il progetto di GrapesJS (<c>editor.getProjectData()</c>) e il documento CMS (E-003). La conversione è
/// deterministica e **non perde nulla in silenzio**: ciò che il documento non sa rappresentare (tipi sconosciuti, regole CSS,
/// classi, stili in linea, attributi non previsti, più pagine) è un errore esplicito, non viene scartato. Le sole chiavi ignorate
/// sono metadati puramente editoriali (nomi dei livelli, flag di interazione) che non influiscono sul rendering.
/// Il progetto che si salva non è quello inviato dal browser: è rigenerato dal documento validato (<see cref="AProgetto"/>),
/// perciò il browser non può far memorizzare nel progetto nulla che il documento non contenga.
/// </summary>
public static class AdapterGrapesJs
{
    public const int ByteMassimiProgetto = 1_048_576;
    public const string PrefissoProprieta = "data-p-";
    public const string PrefissoStile = "data-s-";
    public const string IdPagina = "principale";

    private const int ProfonditaMassimaJson = 48;

    private static readonly HashSet<string> _metadatiEditoriali = new(StringComparer.Ordinal)
    {
        "name", "custom-name", "removable", "draggable", "droppable", "copyable", "selectable", "hoverable",
        "highlightable", "layerable", "badgable", "editable", "locked", "tagName", "void",
    };

    /// <summary>Converte il progetto dell'editor in documento; restituisce falso con l'elenco degli errori se qualcosa non è rappresentabile.</summary>
    public static bool TentaDaProgetto(string? progettoJson, RegistroComponenti registro, out DocumentoCms documento, out IReadOnlyList<ErroreBuilder> errori)
    {
        ArgumentNullException.ThrowIfNull(registro);

        documento = DocumentoCms.Vuoto;
        var trovati = new List<ErroreBuilder>();
        errori = trovati;

        if (string.IsNullOrWhiteSpace(progettoJson))
        {
            trovati.Add(new ErroreBuilder("ProgettoNonValido"));
            return false;
        }

        if (Encoding.UTF8.GetByteCount(progettoJson) > ByteMassimiProgetto)
        {
            trovati.Add(new ErroreBuilder("ProgettoTroppoGrande"));
            return false;
        }

        try
        {
            using var radice = JsonDocument.Parse(progettoJson, new JsonDocumentOptions { MaxDepth = ProfonditaMassimaJson });
            if (radice.RootElement.ValueKind != JsonValueKind.Object)
            {
                trovati.Add(new ErroreBuilder("ProgettoNonValido"));
                return false;
            }

            JsonElement? pagine = null;
            foreach (var campo in radice.RootElement.EnumerateObject())
            {
                switch (campo.Name)
                {
                    case "pages" when campo.Value.ValueKind == JsonValueKind.Array:
                        pagine = campo.Value;
                        break;
                    case "assets" or "styles" or "symbols" or "dataSources":
                        if (campo.Value.ValueKind != JsonValueKind.Array || campo.Value.GetArrayLength() > 0)
                        {
                            trovati.Add(new ErroreBuilder(campo.Name == "styles" ? "StiliCssNonSupportati" : "ElementoNonSupportato", null, campo.Name));
                        }

                        break;
                    default:
                        trovati.Add(new ErroreBuilder("CampoNonSupportato", null, Taglia(campo.Name)));
                        break;
                }
            }

            if (pagine is not { } elencoPagine || elencoPagine.GetArrayLength() != 1)
            {
                trovati.Add(new ErroreBuilder("PagineNonSupportate"));
                return false;
            }

            var componentePrincipale = TrovaComponentePrincipale(elencoPagine[0], trovati);
            if (componentePrincipale is not { } principale)
            {
                return false;
            }

            var nodi = new List<NodoCms>();
            foreach (var campo in principale.EnumerateObject())
            {
                switch (campo.Name)
                {
                    case "type" when campo.Value.ValueKind == JsonValueKind.String && campo.Value.GetString() == "wrapper":
                        break;
                    case "stylable":
                        // Metadato di GrapesJS sul contenitore (proprietà CSS modificabili): il documento non ha CSS libero, nulla da convertire.
                        break;
                    case "head" when EOggettoCosi(campo.Value, "type", "head"):
                    case "docEl" when EOggettoCosi(campo.Value, "tagName", "html"):
                        // Scheletro del documento del canvas (head e html) scritto da GrapesJS 0.22+: solo la forma vuota e nota.
                        break;
                    case "components" when campo.Value.ValueKind == JsonValueKind.Array:
                        nodi = ConvertiElenco(campo.Value, registro, "n", trovati);
                        break;
                    case "attributes" when campo.Value.ValueKind == JsonValueKind.Object:
                        VerificaAttributiContenitore(campo.Value, trovati);
                        break;
                    default:
                        if (!_metadatiEditoriali.Contains(campo.Name))
                        {
                            trovati.Add(new ErroreBuilder("CampoNonSupportato", null, Taglia(campo.Name)));
                        }

                        break;
                }
            }

            if (trovati.Count > 0)
            {
                return false;
            }

            documento = new DocumentoCms(DocumentoCms.VersioneCorrente, nodi);
            return true;
        }
        catch (JsonException)
        {
            trovati.Add(new ErroreBuilder("JsonNonValido"));
            return false;
        }
    }

    /// <summary>Rigenera il progetto dell'editor dal documento: stesso documento, stessa stringa.</summary>
    public static string AProgetto(DocumentoCms documento)
    {
        ArgumentNullException.ThrowIfNull(documento);

        using var flusso = new MemoryStream();
        using (var scrittore = new Utf8JsonWriter(flusso))
        {
            scrittore.WriteStartObject();
            scrittore.WriteStartArray("assets");
            scrittore.WriteEndArray();
            scrittore.WriteStartArray("styles");
            scrittore.WriteEndArray();
            scrittore.WriteStartArray("pages");
            scrittore.WriteStartObject();
            scrittore.WriteString("id", IdPagina);
            scrittore.WriteStartArray("frames");
            scrittore.WriteStartObject();
            scrittore.WriteStartObject("component");
            scrittore.WriteString("type", "wrapper");
            scrittore.WriteStartArray("components");
            foreach (var nodo in documento.Nodi)
            {
                ScriviNodo(scrittore, nodo);
            }

            scrittore.WriteEndArray();
            scrittore.WriteEndObject();
            scrittore.WriteEndObject();
            scrittore.WriteEndArray();
            scrittore.WriteEndObject();
            scrittore.WriteEndArray();
            scrittore.WriteStartArray("symbols");
            scrittore.WriteEndArray();
            scrittore.WriteStartArray("dataSources");
            scrittore.WriteEndArray();
            scrittore.WriteEndObject();
        }

        return Encoding.UTF8.GetString(flusso.ToArray());
    }

    /// <summary>Nome dell'attributo che porta la proprietà (<c>idRisorsa</c> → <c>data-p-id-risorsa</c>).</summary>
    public static string NomeAttributoProprieta(string nome) => PrefissoProprieta + AKebab(nome);

    public static string NomeAttributoStile(string nome) => PrefissoStile + AKebab(nome);

    private static string AKebab(string nome)
    {
        var risultato = new StringBuilder(nome.Length + 4);
        foreach (var carattere in nome)
        {
            if (char.IsUpper(carattere))
            {
                risultato.Append('-').Append(char.ToLowerInvariant(carattere));
            }
            else
            {
                risultato.Append(carattere);
            }
        }

        return risultato.ToString();
    }

    /// <summary>Vero se l'elemento è esattamente <c>{"chiave":"valore"}</c>: l'unica forma di quei metadati che si accetta.</summary>
    private static bool EOggettoCosi(JsonElement elemento, string chiave, string valore)
    {
        if (elemento.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var proprieta = elemento.EnumerateObject().ToList();
        return proprieta.Count == 1 && proprieta[0].Name == chiave && proprieta[0].Value.ValueKind == JsonValueKind.String && proprieta[0].Value.GetString() == valore;
    }

    private static JsonElement? TrovaComponentePrincipale(JsonElement pagina, List<ErroreBuilder> errori)
    {
        if (pagina.ValueKind != JsonValueKind.Object)
        {
            errori.Add(new ErroreBuilder("PagineNonSupportate"));
            return null;
        }

        JsonElement? frames = null;
        foreach (var campo in pagina.EnumerateObject())
        {
            switch (campo.Name)
            {
                case "frames" when campo.Value.ValueKind == JsonValueKind.Array:
                    frames = campo.Value;
                    break;
                case "id" or "type" or "name":
                    break;
                default:
                    errori.Add(new ErroreBuilder("CampoNonSupportato", null, Taglia(campo.Name)));
                    break;
            }
        }

        if (frames is not { } elencoFrame || elencoFrame.GetArrayLength() != 1 || elencoFrame[0].ValueKind != JsonValueKind.Object)
        {
            errori.Add(new ErroreBuilder("PagineNonSupportate"));
            return null;
        }

        JsonElement? componente = null;
        foreach (var campo in elencoFrame[0].EnumerateObject())
        {
            switch (campo.Name)
            {
                case "component" when campo.Value.ValueKind == JsonValueKind.Object:
                    componente = campo.Value;
                    break;
                case "id":
                    break;
                default:
                    errori.Add(new ErroreBuilder("CampoNonSupportato", null, Taglia(campo.Name)));
                    break;
            }
        }

        if (componente is null)
        {
            errori.Add(new ErroreBuilder("ProgettoNonValido"));
        }

        return componente;
    }

    private static void VerificaAttributiContenitore(JsonElement attributi, List<ErroreBuilder> errori)
    {
        foreach (var attributo in attributi.EnumerateObject().Where(a => a.Name != "id"))
        {
            errori.Add(new ErroreBuilder("AttributoNonSupportato", null, Taglia(attributo.Name)));
        }
    }

    private static List<NodoCms> ConvertiElenco(JsonElement elenco, RegistroComponenti registro, string percorso, List<ErroreBuilder> errori)
    {
        var nodi = new List<NodoCms>();
        var indice = 0;
        foreach (var elemento in elenco.EnumerateArray())
        {
            var percorsoNodo = $"{percorso}-{indice}";
            indice++;
            if (elemento.ValueKind != JsonValueKind.Object)
            {
                errori.Add(new ErroreBuilder("ComponenteNonValido", percorsoNodo));
                continue;
            }

            if (Converti(elemento, registro, percorsoNodo, errori) is { } nodo)
            {
                nodi.Add(nodo);
            }
        }

        return nodi;
    }

    private static NodoCms? Converti(JsonElement componente, RegistroComponenti registro, string percorso, List<ErroreBuilder> errori)
    {
        string? tipo = null;
        JsonElement? attributi = null;
        JsonElement? figli = null;
        foreach (var campo in componente.EnumerateObject())
        {
            switch (campo.Name)
            {
                case "type" when campo.Value.ValueKind == JsonValueKind.String:
                    tipo = campo.Value.GetString();
                    break;
                case "attributes" when campo.Value.ValueKind == JsonValueKind.Object:
                    attributi = campo.Value;
                    break;
                case "components" when campo.Value.ValueKind == JsonValueKind.Array:
                    figli = campo.Value;
                    break;
                case "classes" or "style" when campo.Value.ValueKind == JsonValueKind.Array && campo.Value.GetArrayLength() == 0:
                case "style" when campo.Value.ValueKind == JsonValueKind.Object && !campo.Value.EnumerateObject().Any():
                    break;
                case "classes":
                    errori.Add(new ErroreBuilder("ClassiNonSupportate", percorso));
                    break;
                case "style":
                    errori.Add(new ErroreBuilder("StileInLineaNonSupportato", percorso));
                    break;
                default:
                    if (!_metadatiEditoriali.Contains(campo.Name))
                    {
                        errori.Add(new ErroreBuilder("CampoNonSupportato", percorso, Taglia(campo.Name)));
                    }

                    break;
            }
        }

        var definizione = registro.Trova(tipo);
        if (definizione is null || tipo is null)
        {
            errori.Add(new ErroreBuilder("TipoNonSupportato", percorso));
            return null;
        }

        string? id = null;
        var proprieta = new Dictionary<string, string>(StringComparer.Ordinal);
        var stili = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributi is { } elencoAttributi)
        {
            foreach (var attributo in elencoAttributi.EnumerateObject())
            {
                if (attributo.Value.ValueKind != JsonValueKind.String)
                {
                    errori.Add(new ErroreBuilder("AttributoNonValido", percorso, Taglia(attributo.Name)));
                    continue;
                }

                var valore = Normalizza(attributo.Value.GetString()!);
                if (attributo.Name == "id")
                {
                    id = valore;
                }
                else if (attributo.Name.StartsWith(PrefissoProprieta, StringComparison.Ordinal)
                    && definizione.Proprieta.FirstOrDefault(d => NomeAttributoProprieta(d.Nome) == attributo.Name) is { } proprietaDefinita)
                {
                    proprieta[proprietaDefinita.Nome] = valore;
                }
                else if (attributo.Name.StartsWith(PrefissoStile, StringComparison.Ordinal)
                    && definizione.Stili.FirstOrDefault(d => NomeAttributoStile(d.Nome) == attributo.Name) is { } stileDefinito)
                {
                    stili[stileDefinito.Nome] = valore;
                }
                else
                {
                    errori.Add(new ErroreBuilder("AttributoNonSupportato", id ?? percorso, Taglia(attributo.Name)));
                }
            }
        }

        var nodiFigli = figli is { } elencoFigli ? ConvertiElenco(elencoFigli, registro, percorso, errori) : [];
        return new NodoCms(string.IsNullOrEmpty(id) ? $"auto-{percorso}" : id, tipo, proprieta, stili, nodiFigli);
    }

    private static string Normalizza(string valore) => valore.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static void ScriviNodo(Utf8JsonWriter scrittore, NodoCms nodo)
    {
        scrittore.WriteStartObject();
        scrittore.WriteString("type", nodo.Tipo);
        scrittore.WriteStartObject("attributes");
        scrittore.WriteString("id", nodo.Id);
        foreach (var coppia in nodo.Proprieta.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            scrittore.WriteString(NomeAttributoProprieta(coppia.Key), coppia.Value);
        }

        foreach (var coppia in nodo.Stili.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            scrittore.WriteString(NomeAttributoStile(coppia.Key), coppia.Value);
        }

        scrittore.WriteEndObject();
        if (nodo.Figli.Count > 0)
        {
            scrittore.WriteStartArray("components");
            foreach (var figlio in nodo.Figli)
            {
                ScriviNodo(scrittore, figlio);
            }

            scrittore.WriteEndArray();
        }

        scrittore.WriteEndObject();
    }

    private static string Taglia(string testo) => testo.Length > RegistroComponenti.LunghezzaMassimaId ? testo[..RegistroComponenti.LunghezzaMassimaId] : testo;
}

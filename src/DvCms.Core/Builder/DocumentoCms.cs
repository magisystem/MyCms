using System.Text.Json;
using DvCms.DTO.Builder;

namespace DvCms.Core.Builder;

/// <summary>
/// Lettura e scrittura del documento CMS in JSON canonico: stesse informazioni, stessa stringa (chiavi in ordine, nessuno spazio),
/// così il confronto e l'impronta sono deterministici. La lettura è rigida: campi sconosciuti, tipi diversi dal previsto e
/// chiavi duplicate sono errori, non vengono ignorati.
/// </summary>
public static class SerializzatoreDocumento
{
    public const int ProfonditaMassimaJson = 32;

    public static string Scrivi(DocumentoCms documento)
    {
        ArgumentNullException.ThrowIfNull(documento);

        using var flusso = new MemoryStream();
        using (var scrittore = new Utf8JsonWriter(flusso))
        {
            scrittore.WriteStartObject();
            scrittore.WriteNumber("versioneSchema", documento.VersioneSchema);
            scrittore.WriteStartArray("nodi");
            foreach (var nodo in documento.Nodi)
            {
                ScriviNodo(scrittore, nodo);
            }

            scrittore.WriteEndArray();
            scrittore.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(flusso.ToArray());
    }

    public static bool TentaLeggi(string? json, out DocumentoCms documento, out IReadOnlyList<ErroreBuilder> errori)
    {
        documento = DocumentoCms.Vuoto;
        var trovati = new List<ErroreBuilder>();
        errori = trovati;

        if (string.IsNullOrWhiteSpace(json))
        {
            trovati.Add(new ErroreBuilder("DocumentoNonValido"));
            return false;
        }

        try
        {
            using var radice = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = ProfonditaMassimaJson });
            if (radice.RootElement.ValueKind != JsonValueKind.Object)
            {
                trovati.Add(new ErroreBuilder("DocumentoNonValido"));
                return false;
            }

            int? versione = null;
            List<NodoCms>? nodi = null;
            foreach (var proprieta in radice.RootElement.EnumerateObject())
            {
                switch (proprieta.Name)
                {
                    case "versioneSchema" when proprieta.Value.ValueKind == JsonValueKind.Number && proprieta.Value.TryGetInt32(out var v):
                        versione = v;
                        break;
                    case "nodi" when proprieta.Value.ValueKind == JsonValueKind.Array:
                        nodi = LeggiNodi(proprieta.Value, trovati);
                        break;
                    default:
                        trovati.Add(new ErroreBuilder("CampoNonPrevisto", null, Taglia(proprieta.Name)));
                        break;
                }
            }

            if (versione is null || nodi is null)
            {
                trovati.Add(new ErroreBuilder("DocumentoNonValido"));
            }

            if (trovati.Count > 0)
            {
                return false;
            }

            documento = new DocumentoCms(versione!.Value, nodi!);
            return true;
        }
        catch (JsonException)
        {
            trovati.Add(new ErroreBuilder("JsonNonValido"));
            return false;
        }
    }

    private static List<NodoCms> LeggiNodi(JsonElement array, List<ErroreBuilder> errori)
    {
        var nodi = new List<NodoCms>();
        foreach (var elemento in array.EnumerateArray())
        {
            if (elemento.ValueKind != JsonValueKind.Object)
            {
                errori.Add(new ErroreBuilder("NodoNonValido"));
                continue;
            }

            string? id = null;
            string? tipo = null;
            IReadOnlyDictionary<string, string> proprieta = new Dictionary<string, string>();
            IReadOnlyDictionary<string, string> stili = new Dictionary<string, string>();
            List<NodoCms> figli = [];
            foreach (var campo in elemento.EnumerateObject())
            {
                switch (campo.Name)
                {
                    case "id" when campo.Value.ValueKind == JsonValueKind.String:
                        id = campo.Value.GetString();
                        break;
                    case "tipo" when campo.Value.ValueKind == JsonValueKind.String:
                        tipo = campo.Value.GetString();
                        break;
                    case "proprieta" when campo.Value.ValueKind == JsonValueKind.Object:
                        proprieta = LeggiDizionario(campo.Value, id, campo.Name, errori);
                        break;
                    case "stili" when campo.Value.ValueKind == JsonValueKind.Object:
                        stili = LeggiDizionario(campo.Value, id, campo.Name, errori);
                        break;
                    case "figli" when campo.Value.ValueKind == JsonValueKind.Array:
                        figli = LeggiNodi(campo.Value, errori);
                        break;
                    default:
                        errori.Add(new ErroreBuilder("CampoNonPrevisto", Taglia(id), Taglia(campo.Name)));
                        break;
                }
            }

            if (id is null || tipo is null)
            {
                errori.Add(new ErroreBuilder("NodoNonValido", Taglia(id)));
                continue;
            }

            nodi.Add(new NodoCms(id, tipo, proprieta, stili, figli));
        }

        return nodi;
    }

    private static Dictionary<string, string> LeggiDizionario(JsonElement oggetto, string? idNodo, string campo, List<ErroreBuilder> errori)
    {
        var risultato = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var coppia in oggetto.EnumerateObject())
        {
            if (coppia.Value.ValueKind != JsonValueKind.String || !risultato.TryAdd(coppia.Name, coppia.Value.GetString()!))
            {
                errori.Add(new ErroreBuilder("ValoreNonValido", Taglia(idNodo), Taglia($"{campo}.{coppia.Name}")));
            }
        }

        return risultato;
    }

    private static void ScriviNodo(Utf8JsonWriter scrittore, NodoCms nodo)
    {
        scrittore.WriteStartObject();
        scrittore.WriteString("id", nodo.Id);
        scrittore.WriteString("tipo", nodo.Tipo);
        scrittore.WriteStartObject("proprieta");
        foreach (var coppia in nodo.Proprieta.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            scrittore.WriteString(coppia.Key, coppia.Value);
        }

        scrittore.WriteEndObject();
        scrittore.WriteStartObject("stili");
        foreach (var coppia in nodo.Stili.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            scrittore.WriteString(coppia.Key, coppia.Value);
        }

        scrittore.WriteEndObject();
        scrittore.WriteStartArray("figli");
        foreach (var figlio in nodo.Figli)
        {
            ScriviNodo(scrittore, figlio);
        }

        scrittore.WriteEndArray();
        scrittore.WriteEndObject();
    }

    private static string? Taglia(string? testo) => testo is { Length: > 64 } ? testo[..64] : testo;
}

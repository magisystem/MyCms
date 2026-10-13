using System.Text.Json;

namespace DvCms.DTO.Builder;

/// <summary>Un nodo del documento CMS: un componente del catalogo con le sue proprietà, i suoi stili e i suoi figli.</summary>
/// <param name="Id">Identificativo stabile nel documento (univoco): i modelli di pagina lo usano per riconoscere i nodi bloccati.</param>
/// <param name="Tipo">Tipo di componente del catalogo, ad esempio <c>contenuto.titolo</c>.</param>
/// <param name="Proprieta">Valori delle proprietà del componente (solo testo, validati dal registro).</param>
/// <param name="Stili">Scelte grafiche ammesse dal componente (token e varianti, mai CSS libero).</param>
/// <param name="Figli">Nodi contenuti, nell'ordine di visualizzazione.</param>
public sealed record NodoCms(
    string Id,
    string Tipo,
    IReadOnlyDictionary<string, string> Proprieta,
    IReadOnlyDictionary<string, string> Stili,
    IReadOnlyList<NodoCms> Figli);

/// <summary>Il documento CMS canonico (ADR-0005): un albero tipizzato e versionato, l'unica cosa che il renderer disegna.</summary>
public sealed record DocumentoCms(int VersioneSchema, IReadOnlyList<NodoCms> Nodi)
{
    public const int VersioneCorrente = 1;

    public static DocumentoCms Vuoto { get; } = new(VersioneCorrente, []);

    /// <summary>Tutti i nodi in ordine di visita (padre prima dei figli).</summary>
    public IEnumerable<NodoCms> Visita()
    {
        var pila = new Stack<NodoCms>(Nodi.Reverse());
        while (pila.Count > 0)
        {
            var nodo = pila.Pop();
            yield return nodo;
            foreach (var figlio in nodo.Figli.Reverse())
            {
                pila.Push(figlio);
            }
        }
    }
}

/// <summary>Un problema riscontrato: codice stabile, nodo coinvolto e campo. Mai valori dei campi (possono essere ostili o riservati).</summary>
public sealed record ErroreBuilder(string Codice, string? IdNodo = null, string? Campo = null);

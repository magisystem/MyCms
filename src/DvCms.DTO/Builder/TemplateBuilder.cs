using System.Security.Cryptography;
using System.Text;

namespace DvCms.DTO.Builder;

/// <summary>Uno spazio in cui chi scrive può aggiungere contenuti: solo i tipi elencati (come figli diretti) e fino a un massimo.</summary>
public sealed record SlotTemplate(IReadOnlyList<string> TipiAmmessi, int FigliMassimi);

/// <summary>
/// Un nodo della struttura di un template. Tutto ciò che il template definisce è **bloccato**: il nodo deve esserci, con lo
/// stesso tipo, nello stesso posto, con le stesse proprietà e gli stessi stili, salvo le proprietà elencate come modificabili.
/// Se il nodo è uno <see cref="Slot"/>, i suoi figli sono liberi (entro i tipi ammessi); altrimenti i figli sono quelli del template.
/// </summary>
public sealed record NodoTemplate(
    string Id,
    string Tipo,
    IReadOnlyDictionary<string, string>? Proprieta = null,
    IReadOnlyDictionary<string, string>? Stili = null,
    IReadOnlyList<string>? ProprietaModificabili = null,
    IReadOnlyList<NodoTemplate>? Figli = null,
    SlotTemplate? Slot = null);

/// <summary>
/// Un template del builder in una versione precisa (immutabile, come i modelli di pagina). Definisce la struttura iniziale e la
/// policy di editing: o una radice fissa con nodi bloccati e slot, oppure uno slot radice (pagina libera).
/// </summary>
public sealed record TemplateBuilder(string Id, int Versione, IReadOnlyList<NodoTemplate> Radice, SlotTemplate? SlotRadice = null)
{
    public DocumentoCms DocumentoIniziale() => new(DocumentoCms.VersioneCorrente, [.. Radice.Select(Costruisci)]);

    /// <summary>Impronta (SHA-256 esadecimale) di struttura e policy: un test la fissa per ogni versione rilasciata.</summary>
    public string Impronta
    {
        get
        {
            var testo = new StringBuilder().Append(Id).Append('|').Append(Versione).Append('|');
            if (SlotRadice is not null)
            {
                testo.Append("radice:").Append(Slot(SlotRadice)).Append('|');
            }

            foreach (var nodo in Radice)
            {
                Aggiungi(testo, nodo);
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo.ToString())));
        }
    }

    private static NodoCms Costruisci(NodoTemplate nodo) => new(
        nodo.Id,
        nodo.Tipo,
        nodo.Proprieta ?? new Dictionary<string, string>(),
        nodo.Stili ?? new Dictionary<string, string>(),
        [.. (nodo.Figli ?? []).Select(Costruisci)]);

    private static string Slot(SlotTemplate slot) => $"{string.Join(',', slot.TipiAmmessi)}/{slot.FigliMassimi}";

    private static void Aggiungi(StringBuilder testo, NodoTemplate nodo)
    {
        testo.Append('(').Append(nodo.Id).Append(':').Append(nodo.Tipo).Append(':');
        foreach (var coppia in (nodo.Proprieta ?? new Dictionary<string, string>()).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            testo.Append(coppia.Key).Append('=').Append(coppia.Value).Append(',');
        }

        testo.Append(':');
        foreach (var coppia in (nodo.Stili ?? new Dictionary<string, string>()).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            testo.Append(coppia.Key).Append('=').Append(coppia.Value).Append(',');
        }

        testo.Append(':').Append(string.Join(',', nodo.ProprietaModificabili ?? [])).Append(':');
        if (nodo.Slot is not null)
        {
            testo.Append("slot:").Append(Slot(nodo.Slot));
        }

        foreach (var figlio in nodo.Figli ?? [])
        {
            Aggiungi(testo, figlio);
        }

        testo.Append(')');
    }
}

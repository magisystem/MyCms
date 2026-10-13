using System.Text;
using System.Text.Json;
using DvCms.DTO.Builder;

namespace DvCms.Core.Builder;

/// <summary>
/// Ciò che il browser riceve per configurare l'editor: il catalogo dei componenti (derivato dal registro, così non esistono due
/// elenchi da tenere allineati) e i vincoli del template. Servono solo a guidare l'interfaccia (nodi bloccati non trascinabili,
/// slot che accettano certi tipi): l'autorità resta il server, che riconfronta tutto al salvataggio.
/// </summary>
public static class CatalogoEditor
{
    public static string Catalogo(RegistroComponenti registro)
    {
        ArgumentNullException.ThrowIfNull(registro);

        using var flusso = new MemoryStream();
        using (var scrittore = new Utf8JsonWriter(flusso))
        {
            scrittore.WriteStartArray();
            foreach (var componente in registro.Tutti)
            {
                scrittore.WriteStartObject();
                scrittore.WriteString("tipo", componente.Tipo);
                scrittore.WriteStartArray("proprieta");
                foreach (var proprieta in componente.Proprieta)
                {
                    Scrivi(scrittore, proprieta, AdapterGrapesJs.NomeAttributoProprieta(proprieta.Nome));
                }

                scrittore.WriteEndArray();
                scrittore.WriteStartArray("stili");
                foreach (var stile in componente.Stili)
                {
                    Scrivi(scrittore, stile, AdapterGrapesJs.NomeAttributoStile(stile.Nome));
                }

                scrittore.WriteEndArray();
                scrittore.WriteStartArray("figliAmmessi");
                foreach (var figlio in componente.FigliAmmessi)
                {
                    scrittore.WriteStringValue(figlio);
                }

                scrittore.WriteEndArray();
                scrittore.WriteNumber("figliMinimi", componente.FigliMinimi);
                scrittore.WriteNumber("figliMassimi", componente.FigliMassimi);
                scrittore.WriteEndObject();
            }

            scrittore.WriteEndArray();
        }

        return Encoding.UTF8.GetString(flusso.ToArray());
    }

    /// <summary>Vincoli del template: per ogni nodo bloccato le proprietà modificabili, per ogni slot i tipi ammessi.</summary>
    public static string Vincoli(TemplateBuilder template)
    {
        ArgumentNullException.ThrowIfNull(template);

        using var flusso = new MemoryStream();
        using (var scrittore = new Utf8JsonWriter(flusso))
        {
            scrittore.WriteStartObject();
            if (template.SlotRadice is { } radice)
            {
                scrittore.WritePropertyName("radice");
                ScriviSlot(scrittore, radice);
            }
            else
            {
                scrittore.WriteNull("radice");
            }

            scrittore.WriteStartObject("nodi");
            foreach (var nodo in template.Radice)
            {
                ScriviVincolo(scrittore, nodo);
            }

            scrittore.WriteEndObject();
            scrittore.WriteEndObject();
        }

        return Encoding.UTF8.GetString(flusso.ToArray());
    }

    private static void ScriviVincolo(Utf8JsonWriter scrittore, NodoTemplate nodo)
    {
        scrittore.WritePropertyName(nodo.Id);
        scrittore.WriteStartObject();
        scrittore.WriteBoolean("bloccato", true);
        scrittore.WriteStartArray("proprietaModificabili");
        foreach (var nome in nodo.ProprietaModificabili ?? [])
        {
            scrittore.WriteStringValue(nome);
        }

        scrittore.WriteEndArray();
        if (nodo.Slot is { } slot)
        {
            scrittore.WritePropertyName("slot");
            ScriviSlot(scrittore, slot);
        }
        else
        {
            scrittore.WriteNull("slot");
        }

        scrittore.WriteEndObject();
        if (nodo.Slot is null)
        {
            foreach (var figlio in nodo.Figli ?? [])
            {
                ScriviVincolo(scrittore, figlio);
            }
        }
    }

    private static void ScriviSlot(Utf8JsonWriter scrittore, SlotTemplate slot)
    {
        scrittore.WriteStartObject();
        scrittore.WriteStartArray("tipiAmmessi");
        foreach (var tipo in slot.TipiAmmessi)
        {
            scrittore.WriteStringValue(tipo);
        }

        scrittore.WriteEndArray();
        scrittore.WriteNumber("figliMassimi", slot.FigliMassimi);
        scrittore.WriteEndObject();
    }

    private static void Scrivi(Utf8JsonWriter scrittore, DefinizioneProprieta definizione, string attributo)
    {
        scrittore.WriteStartObject();
        scrittore.WriteString("nome", definizione.Nome);
        scrittore.WriteString("attributo", attributo);
        scrittore.WriteString("tipo", definizione.Tipo.ToString());
        scrittore.WriteBoolean("obbligatoria", definizione.Obbligatoria);
        scrittore.WriteNumber("lunghezzaMassima", definizione.LunghezzaMassima);
        scrittore.WriteString("predefinito", definizione.Predefinito);
        scrittore.WriteStartArray("valori");
        foreach (var valore in definizione.Valori ?? [])
        {
            scrittore.WriteStringValue(valore);
        }

        scrittore.WriteEndArray();
        scrittore.WriteEndObject();
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using DvCms.DTO.Moduli;

namespace DvCms.Core.Moduli;

/// <summary>Lettura e scrittura in JSON della definizione e delle impostazioni di un modulo (colonne <c>DefinizioneJson</c> e <c>ImpostazioniJson</c>).</summary>
public static class SerializzatoreModulo
{
    private static readonly JsonSerializerOptions _opzioni = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Scrivi(DefinizioneModulo definizione) => JsonSerializer.Serialize(definizione, _opzioni);

    public static string Scrivi(ImpostazioniModulo impostazioni) => JsonSerializer.Serialize(impostazioni, _opzioni);

    /// <summary>Legge la definizione salvata; se il JSON è guasto restituisce un modulo vuoto invece di far fallire la pagina.</summary>
    public static DefinizioneModulo LeggiDefinizione(string json, string linguaPredefinita)
    {
        try
        {
            return JsonSerializer.Deserialize<DefinizioneModulo>(json, _opzioni) ?? DefinizioneModulo.Vuota(linguaPredefinita);
        }
        catch (JsonException)
        {
            return DefinizioneModulo.Vuota(linguaPredefinita);
        }
    }

    public static ImpostazioniModulo LeggiImpostazioni(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ImpostazioniModulo>(json, _opzioni) ?? ImpostazioniModulo.Predefinite;
        }
        catch (JsonException)
        {
            return ImpostazioniModulo.Predefinite;
        }
    }
}

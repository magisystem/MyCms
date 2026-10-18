namespace DvCms.Core.Moduli;

/// <summary>Scelta del testo di un modulo per lingua: quella richiesta, altrimenti la predefinita del sito, altrimenti vuoto.</summary>
public static class TestiModulo
{
    public static string Per(IReadOnlyDictionary<string, string> testi, string lingua, string predefinita)
    {
        ArgumentNullException.ThrowIfNull(testi);

        if (testi.TryGetValue(lingua, out var testo) && !string.IsNullOrWhiteSpace(testo))
        {
            return testo;
        }

        return testi.TryGetValue(predefinita, out var ripiego) ? ripiego : string.Empty;
    }
}

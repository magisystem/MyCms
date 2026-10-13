using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace DvCms.Core.Configurazione;

public static class LetturaConfigurazione
{
    /// <summary>Legge un intero di configurazione; se manca restituisce il predefinito, se non è un intero nell'intervallo ferma l'avvio.</summary>
    public static int LeggiIntero(this IConfiguration configurazione, string chiave, int predefinito, int minimo, int massimo)
    {
        ArgumentNullException.ThrowIfNull(configurazione);

        var testo = configurazione[chiave];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return predefinito;
        }

        if (!int.TryParse(testo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valore) || valore < minimo || valore > massimo)
        {
            throw new InvalidOperationException($"Valore non valido per '{chiave}': atteso un intero tra {minimo} e {massimo}.");
        }

        return valore;
    }
}

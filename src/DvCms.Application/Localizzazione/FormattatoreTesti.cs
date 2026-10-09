using System.Globalization;

namespace DvCms.Application.Localizzazione;

/// <summary>Applica gli argomenti (formato composito) a un testo, senza mai propagare errori di formato.</summary>
public static class FormattatoreTesti
{
    public static string Formatta(string modello, CultureInfo cultura, object?[] argomenti)
    {
        ArgumentNullException.ThrowIfNull(modello);
        ArgumentNullException.ThrowIfNull(cultura);

        if (argomenti is null || argomenti.Length == 0)
        {
            return modello;
        }

        try
        {
            return string.Format(cultura, modello, argomenti);
        }
        catch (FormatException)
        {
            // Una traduzione modificata male (segnaposto errati) non deve rompere la pagina.
            return modello;
        }
    }
}

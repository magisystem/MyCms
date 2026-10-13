using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using DvCms.DTO.Email;

namespace DvCms.Emails;

/// <summary>
/// Controlla un modello prima di salvarlo e lo rende con i dati d'esempio per l'anteprima (ADR-0025). Un modello è valido se è ben
/// formato, usa solo variabili del suo catalogo, non contiene elementi attivi nell'HTML e, nei modelli critici, riporta le variabili
/// obbligatorie (il codice o il link) sia nell'HTML sia nel testo: senza, l'email non servirebbe a nulla.
/// </summary>
public static partial class ValidatoreModelloEmail
{
    public const int LunghezzaMassimaOggetto = 200;
    public const int LunghezzaMassimaHtml = 100_000;
    public const int LunghezzaMassimaTesto = 50_000;

    [GeneratedRegex(@"<\s*(script|iframe|object|embed|form|link|meta|base|style)\b|javascript\s*:|<[^>]*\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex ElementiAttivi();

    public static async Task<AnteprimaModelloEmail> RendiAsync(DefinizioneModelloEmail definizione, ContenutoModelloEmail contenuto)
    {
        ArgumentNullException.ThrowIfNull(definizione);
        ArgumentNullException.ThrowIfNull(contenuto);

        var errori = new List<string>();
        ControllaForma(contenuto, errori);

        string? oggetto = null, html = null, testo = null;
        if (errori.Count == 0)
        {
            var esempio = CatalogoModelliEmail.Esempio(definizione.Codice);
            var indefinite = new HashSet<string>(StringComparer.Ordinal);
            oggetto = await RendiParteAsync("Oggetto", errori, () => MotoreModelliEmail.RenderizzaOggettoAsync(contenuto.Oggetto, esempio, indefinite));
            html = await RendiParteAsync("Corpo HTML", errori, () => MotoreModelliEmail.RenderizzaAsync(contenuto.CorpoHtml, esempio, html: true, indefinite));
            testo = await RendiParteAsync("Corpo testo", errori, () => MotoreModelliEmail.RenderizzaAsync(contenuto.CorpoTesto, esempio, html: false, indefinite));

            foreach (var nome in indefinite.Order(StringComparer.Ordinal))
            {
                errori.Add($"La variabile «{nome}» non esiste in questo modello: usa solo quelle elencate.");
            }

            if (oggetto is not null && oggetto.Length == 0)
            {
                errori.Add("L'oggetto reso è vuoto.");
            }

            foreach (var variabile in definizione.Variabili.Where(v => v.Obbligatoria))
            {
                var valore = Convert.ToString(esempio[variabile.Nome], CultureInfo.InvariantCulture) ?? string.Empty;
                if (html is not null && !html.Contains(HtmlEncoder.Default.Encode(valore), StringComparison.Ordinal))
                {
                    errori.Add($"Nel corpo HTML manca {{{{ {variabile.Nome} }}}}: è obbligatorio.");
                }

                if (testo is not null && !testo.Contains(valore, StringComparison.Ordinal))
                {
                    errori.Add($"Nel corpo testo manca {{{{ {variabile.Nome} }}}}: è obbligatorio.");
                }
            }
        }

        var validazione = new EsitoValidazioneModelloEmail(errori);
        return validazione.Valido
            ? new AnteprimaModelloEmail(validazione, oggetto ?? string.Empty, html ?? string.Empty, testo ?? string.Empty)
            : new AnteprimaModelloEmail(validazione, string.Empty, string.Empty, string.Empty);
    }

    private static void ControllaForma(ContenutoModelloEmail contenuto, List<string> errori)
    {
        ControllaParte("Oggetto", contenuto.Oggetto, LunghezzaMassimaOggetto, errori);
        ControllaParte("Corpo HTML", contenuto.CorpoHtml, LunghezzaMassimaHtml, errori);
        ControllaParte("Corpo testo", contenuto.CorpoTesto, LunghezzaMassimaTesto, errori);

        if (!string.IsNullOrEmpty(contenuto.CorpoHtml) && ElementiAttivi().IsMatch(contenuto.CorpoHtml))
        {
            errori.Add("Corpo HTML: non sono ammessi script, moduli, frame, stili incorporati né gestori di eventi.");
        }
    }

    private static void ControllaParte(string nome, string? sorgente, int massimo, List<string> errori)
    {
        if (string.IsNullOrWhiteSpace(sorgente))
        {
            errori.Add($"{nome}: è obbligatorio.");
            return;
        }

        if (sorgente.Length > massimo)
        {
            errori.Add($"{nome}: supera i {massimo.ToString(CultureInfo.InvariantCulture)} caratteri.");
            return;
        }

        var errore = MotoreModelliEmail.Analizza(sorgente);
        if (errore is not null)
        {
            errori.Add($"{nome}: {errore}");
        }
    }

    private static async Task<string?> RendiParteAsync(string nome, List<string> errori, Func<Task<string>> rendi)
    {
        try
        {
            return await rendi();
        }
        catch (Exception eccezione) when (eccezione is InvalidOperationException or FormatException)
        {
            errori.Add($"{nome}: {eccezione.Message}");
            return null;
        }
    }
}

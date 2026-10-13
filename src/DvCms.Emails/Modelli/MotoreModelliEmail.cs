using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using Fluid;
using Fluid.Values;

namespace DvCms.Emails;

/// <summary>
/// Rende i modelli email scritti in Liquid con Fluid (ADR-0025). I modelli li scrivono i tenant: il motore non esegue codice (niente
/// accesso a membri di oggetti .NET, niente inclusione di file, niente funzioni), vede solo i valori che gli si passano e ha un tetto
/// ai passi eseguiti. Nell'HTML ogni valore è codificato; nel testo e nell'oggetto no.
/// </summary>
public static class MotoreModelliEmail
{
    /// <summary>Tetto ai passi di un'esecuzione: ferma i cicli senza fine senza disturbare i modelli normali.</summary>
    public const int PassiMassimi = 20_000;

    private static readonly FluidParser _analizzatore = new();

    /// <summary>Controlla la sintassi. Restituisce l'errore, oppure <c>null</c> se il modello è ben formato.</summary>
    public static string? Analizza(string sorgente)
    {
        ArgumentNullException.ThrowIfNull(sorgente);
        return _analizzatore.TryParse(sorgente, out _, out var errore) ? null : errore;
    }

    /// <summary>
    /// Rende il modello. Se <paramref name="indefinite"/> è passato vi si raccolgono i nomi delle variabili usate ma non fornite.
    /// </summary>
    /// <exception cref="FormatException">Il modello non è ben formato.</exception>
    /// <exception cref="InvalidOperationException">L'esecuzione ha superato i passi consentiti o non è riuscita.</exception>
    public static async Task<string> RenderizzaAsync(string sorgente, IReadOnlyDictionary<string, object?> variabili, bool html, ICollection<string>? indefinite = null)
    {
        ArgumentNullException.ThrowIfNull(sorgente);
        ArgumentNullException.ThrowIfNull(variabili);

        if (!_analizzatore.TryParse(sorgente, out var modello, out var errore))
        {
            throw new FormatException(errore);
        }

        var opzioni = new TemplateOptions
        {
            CultureInfo = CultureInfo.InvariantCulture,
            MaxSteps = PassiMassimi,
        };
        if (indefinite is not null)
        {
            opzioni.Undefined = nome =>
            {
                indefinite.Add(nome);
                return ValueTask.FromResult<FluidValue>(NilValue.Instance);
            };
        }

        var contesto = new TemplateContext(opzioni);
        foreach (var (nome, valore) in variabili)
        {
            contesto.SetValue(nome, Normalizza(valore));
        }

        try
        {
            return await modello.RenderAsync(contesto, html ? HtmlEncoder.Default : NullEncoder.Default);
        }
        catch (Exception eccezione) when (eccezione is not OperationCanceledException)
        {
            throw new InvalidOperationException("Il modello non si è potuto rendere: " + eccezione.Message, eccezione);
        }
    }

    /// <summary>Rende un oggetto: una riga sola, senza spazi ai margini.</summary>
    public static async Task<string> RenderizzaOggettoAsync(string sorgente, IReadOnlyDictionary<string, object?> variabili, ICollection<string>? indefinite = null)
    {
        var testo = await RenderizzaAsync(sorgente, variabili, html: false, indefinite);
        return string.Join(' ', testo.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Porta i valori ai soli tipi che il motore sa leggere: testi, numeri, sì/no, elenchi e dizionari di questi.</summary>
    private static object? Normalizza(object? valore) => valore switch
    {
        null => null,
        string or bool or int or long or decimal or double or float => valore,
        IReadOnlyDictionary<string, object?> dizionario => dizionario.ToDictionary(c => c.Key, c => Normalizza(c.Value), StringComparer.Ordinal),
        IDictionary<string, object?> dizionario => dizionario.ToDictionary(c => c.Key, c => Normalizza(c.Value), StringComparer.Ordinal),
        IEnumerable elenco => elenco.Cast<object?>().Select(Normalizza).ToList(),
        _ => throw new ArgumentException($"Tipo di valore non consentito per un modello email: {valore.GetType().Name}."),
    };
}

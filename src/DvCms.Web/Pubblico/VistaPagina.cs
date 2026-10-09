namespace DvCms.Web.Pubblico;

/// <summary>Dati per disegnare una pagina del sito pubblico (o la sua anteprima) nel documento HTML condiviso.</summary>
public sealed record VistaPagina(
    string Titolo,
    string CodiceLingua,
    IReadOnlyList<IReadOnlyList<string>> Paragrafi,
    bool NonIndicizzare,
    string? EtichettaAnteprima = null,
    string IdModello = "testo-semplice",
    int VersioneModello = 1,
    IReadOnlyDictionary<string, string>? DatiModello = null)
{
    public string Dato(string nome) => DatiModello is not null && DatiModello.TryGetValue(nome, out var valore) ? valore : string.Empty;

    /// <summary>Divide il testo semplice in paragrafi (riga vuota) e righe: ogni riga sarà codificata da Razor.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> DividiParagrafi(string contenuto)
    {
        ArgumentNullException.ThrowIfNull(contenuto);

        return [.. contenuto
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragrafo => (IReadOnlyList<string>)[.. paragrafo.Split('\n', StringSplitOptions.TrimEntries)])];
    }
}

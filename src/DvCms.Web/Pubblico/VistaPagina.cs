namespace DvCms.Web.Pubblico;

/// <summary>Una versione linguistica di una pagina: indirizzo assoluto, nome per il selettore e se è la corrente.</summary>
public sealed record VoceLingua(string Codice, string Nome, string Url, bool Corrente);

/// <summary>Dati per disegnare una pagina del sito pubblico (o la sua anteprima) nel documento HTML condiviso.</summary>
public sealed record VistaPagina(
    string Titolo,
    string CodiceLingua,
    IReadOnlyList<IReadOnlyList<string>> Paragrafi,
    bool NonIndicizzare,
    string? EtichettaAnteprima = null,
    string IdModello = "testo-semplice",
    int VersioneModello = 1,
    IReadOnlyDictionary<string, string>? DatiModello = null,
    string? UrlCanonico = null,
    IReadOnlyList<VoceLingua>? Lingue = null,
    string? UrlPredefinito = null,
    string? TitoloSeo = null,
    string? DescrizioneSeo = null)
{
    /// <summary>Titolo del documento: quello per i motori di ricerca se presente, altrimenti il titolo della pagina.</summary>
    public string TitoloDocumento => string.IsNullOrWhiteSpace(TitoloSeo) ? Titolo : TitoloSeo;

    /// <summary>Le versioni linguistiche pubblicate (per hreflang e selettore); vuoto se la pagina esiste in una sola lingua.</summary>
    public IReadOnlyList<VoceLingua> VociLingua => Lingue ?? [];

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

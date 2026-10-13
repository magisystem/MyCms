namespace DvCms.DTO.Cms;

/// <summary>Contenuto pubblicato di una pagina, pronto per il rendering pubblico.</summary>
public sealed record PaginaPubblica(
    string Titolo,
    string Contenuto,
    string CodiceLingua,
    string Slug,
    DateTime PubblicatoIlUtc,
    string IdModello,
    int VersioneModello,
    IReadOnlyDictionary<string, string> DatiModello,
    string Percorso,
    IReadOnlyList<TraduzionePubblica> Traduzioni,
    string? TitoloSeo = null,
    string? DescrizioneSeo = null,
    bool NonIndicizzare = false,
    string? DocumentoRenderJson = null);

/// <summary>
/// Esito della richiesta di un indirizzo pubblico: la pagina da mostrare, oppure il percorso (relativo al sito) verso cui
/// reindirizzare in modo permanente, oppure nulla (404).
/// </summary>
public sealed record RisoluzionePagina(PaginaPubblica? Pagina, string? PercorsoReindirizzamento);

/// <summary>Una versione linguistica pubblicata della pagina (anche quella corrente), con il suo percorso canonico.</summary>
public sealed record TraduzionePubblica(string CodiceLingua, string NomeLingua, string Percorso, bool Predefinita);

/// <summary>Una lingua abilitata per il sito dell'host.</summary>
public sealed record LinguaPubblica(string Codice, string Nome, bool Predefinita);

using DvCms.Models.Cms;

namespace DvCms.DTO.Cms;

public sealed record DatiNuovaPagina(
    string? Slug,
    string? Titolo,
    string? Contenuto,
    string? CodiceLingua,
    bool PaginaIniziale,
    string? IdModello = null,
    IReadOnlyDictionary<string, string>? DatiModello = null,
    Guid? IdPaginaOrigine = null,
    string? TitoloSeo = null,
    string? DescrizioneSeo = null,
    bool NonIndicizzare = false);

public sealed record DatiModificaPagina(
    string? Slug,
    string? Titolo,
    string? Contenuto,
    string? Nota,
    bool PaginaIniziale,
    string? TokenConcorrenza,
    IReadOnlyDictionary<string, string>? DatiModello = null,
    string? TitoloSeo = null,
    string? DescrizioneSeo = null,
    bool NonIndicizzare = false);

public sealed record RiepilogoPagina(
    Guid Id,
    string Slug,
    string Titolo,
    string CodiceLingua,
    StatoPagina Stato,
    bool PaginaIniziale,
    bool ModificheNonPubblicate,
    DateTime AggiornatoIlUtc);

public sealed record RevisioneInElenco(
    Guid Id,
    int Numero,
    string Titolo,
    string? Nota,
    string? Autore,
    DateTime CreatoIlUtc,
    bool Pubblicata,
    bool UltimaBozza,
    bool AutoreDiPiattaforma = false);

/// <summary>Un'altra versione linguistica della stessa pagina (stesso gruppo di traduzione).</summary>
public sealed record TraduzionePagina(Guid IdPagina, string CodiceLingua, string Slug, StatoPagina Stato);

/// <summary>Reindirizzamento permanente da un vecchio slug della pagina, lasciato da un cambio di slug.</summary>
public sealed record ReindirizzamentoPagina(Guid Id, string SlugOrigine, DateTime CreatoIlUtc);

public sealed record DettaglioPagina(
    Guid Id,
    string Slug,
    string CodiceLingua,
    StatoPagina Stato,
    bool PaginaIniziale,
    string Titolo,
    string Contenuto,
    string TokenConcorrenza,
    bool ModificheNonPubblicate,
    IReadOnlyList<RevisioneInElenco> Revisioni,
    string IdModello,
    int VersioneModello,
    IReadOnlyDictionary<string, string> DatiModello,
    int? VersioneModelloRecente,
    IReadOnlyList<TraduzionePagina> Traduzioni,
    IReadOnlyList<LinguaDisponibile> LingueMancanti,
    string? TitoloSeo = null,
    string? DescrizioneSeo = null,
    bool NonIndicizzare = false,
    IReadOnlyList<ReindirizzamentoPagina>? Reindirizzamenti = null,
    bool UsaBuilder = false);

/// <summary>Contenuto di una revisione per l'anteprima (la bozza più recente, oppure una revisione precisa).</summary>
public sealed record AnteprimaPagina(
    string Titolo,
    string Contenuto,
    string CodiceLingua,
    int NumeroRevisione,
    bool Pubblicata,
    bool UltimaBozza,
    string IdModello,
    int VersioneModello,
    IReadOnlyDictionary<string, string> DatiModello,
    string? DocumentoRenderJson = null);

public sealed record LinguaDisponibile(string Codice, string Nome);

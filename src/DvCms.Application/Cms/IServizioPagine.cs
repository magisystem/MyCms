using DvCms.Application.Piattaforma;
using DvCms.Domain.Cms;

namespace DvCms.Application.Cms;

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

/// <summary>
/// Gestione delle pagine del sito dell'host corrente (C-002). Lavora sempre sul tenant e sul sito risolti dall'host:
/// nessun identificativo di tenant o sito arriva dal client. L'attore deve essere un <c>TenantAdmin</c> di quel tenant
/// o un <c>PlatformAdmin</c>, verificato sul database a ogni operazione (le scritture negate sono registrate nell'audit,
/// le letture lanciano <see cref="UnauthorizedAccessException"/>).
/// </summary>
public interface IServizioPagine
{
    /// <summary>Le lingue abilitate per il sito dell'host, quella predefinita per prima.</summary>
    Task<IReadOnlyList<LinguaDisponibile>> ElencaLingueAsync(Guid idAttore, CancellationToken annullamento);

    Task<IReadOnlyList<RiepilogoPagina>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    Task<DettaglioPagina?> LeggiAsync(Guid idAttore, Guid idPagina, CancellationToken annullamento);

    Task<EsitoOperazione> CreaAsync(Guid idAttore, DatiNuovaPagina dati, CancellationToken annullamento);

    /// <summary>
    /// Salva una nuova revisione (bozza). Il token è quello letto con la pagina: se la pagina è cambiata nel frattempo
    /// l'esito è <see cref="CodiceEsito.Conflitto"/>. Se la pagina è stata pubblicata e lo slug cambia, il vecchio indirizzo
    /// reindirizza (301) al nuovo; un reindirizzamento il cui slug torna in uso viene tolto.
    /// </summary>
    Task<EsitoOperazione> SalvaAsync(Guid idAttore, Guid idPagina, DatiModificaPagina dati, CancellationToken annullamento);

    /// <summary>
    /// Contenuto per l'anteprima: l'ultima bozza, oppure la revisione indicata (che deve essere di questa pagina).
    /// Restituisce nullo per una pagina o una revisione di un altro tenant o sito, o inesistente.
    /// </summary>
    Task<AnteprimaPagina?> AnteprimaAsync(Guid idAttore, Guid idPagina, Guid? idRevisione, CancellationToken annullamento);

    /// <summary>
    /// Pubblica una revisione precisa, anche precedente (ritorno a una versione passata): sposta il puntatore pubblicato
    /// senza toccare l'ultima bozza, in una transazione con l'evento di pubblicazione.
    /// </summary>
    Task<EsitoOperazione> PubblicaRevisioneAsync(Guid idAttore, Guid idPagina, Guid idRevisione, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>Riporta il contenuto di una revisione passata come nuova bozza (le revisioni restano immutabili).</summary>
    Task<EsitoOperazione> RipristinaComeBozzaAsync(Guid idAttore, Guid idPagina, Guid idRevisione, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>
    /// Porta la bozza all'ultima versione del suo modello (nuova revisione, le vecchie restano come sono). I campi che
    /// la nuova versione non ha più vengono scartati; se i valori restanti non sono validi per la nuova versione, nulla cambia.
    /// </summary>
    Task<EsitoOperazione> AggiornaModelloAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>Toglie un reindirizzamento della pagina (il vecchio indirizzo torna a rispondere 404).</summary>
    Task<EsitoOperazione> EliminaReindirizzamentoAsync(Guid idAttore, Guid idPagina, Guid idReindirizzamento, CancellationToken annullamento);

    /// <summary>Pubblica l'ultima bozza.</summary>
    Task<EsitoOperazione> PubblicaAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, CancellationToken annullamento);

    Task<EsitoOperazione> RitiraAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, CancellationToken annullamento);
}

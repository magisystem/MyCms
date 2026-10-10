using DvCms.Application.Piattaforma;

namespace DvCms.Application.Builder;

/// <summary>Stato del builder per una pagina: il progetto da aprire nell'editor, i vincoli del template e il token di concorrenza.</summary>
public sealed record StatoBuilder(
    Guid IdPagina,
    string Titolo,
    string CodiceLingua,
    string IdTemplate,
    int VersioneTemplate,
    int NumeroRevisione,
    string ProgettoEditorJson,
    string TokenConcorrenza,
    string CatalogoJson,
    string VincoliJson);

public sealed record EsitoSalvataggioBuilder(
    CodiceEsito Codice,
    IReadOnlyList<ErroreBuilder> Errori,
    string? TokenConcorrenza = null,
    string? ProgettoEditorJson = null,
    int NumeroRevisione = 0,
    bool NuovaRevisione = false)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

/// <summary>
/// Salvataggio e lettura del contenuto del page builder (E-003, E-004, E-005). Lavora sul tenant e sul sito risolti dall'host;
/// l'attore (<c>TenantAdmin</c> del tenant o <c>PlatformAdmin</c>) è riverificato sul database a ogni operazione. Al salvataggio il
/// server converte il progetto dell'editor in documento, lo valida (registro dei componenti, policy del template, risorse media),
/// e solo allora scrive una nuova revisione (bozza) con il documento canonico e un progetto **rigenerato** da esso.
/// </summary>
public interface IServizioBuilder
{
    Task<StatoBuilder?> LeggiAsync(Guid idAttore, Guid idPagina, CancellationToken annullamento);

    /// <summary>
    /// Salva la bozza. Il token è quello letto con la pagina (o restituito dal salvataggio precedente): se la pagina è cambiata
    /// nel frattempo l'esito è <see cref="CodiceEsito.Conflitto"/> e nulla viene sovrascritto. Se il documento non cambia non si
    /// crea alcuna revisione. Un salvataggio **automatico** (<paramref name="automatico"/>) crea una revisione marcata come tale; se in
    /// cima alla cronologia c'è già una revisione automatica dello stesso autore, non pubblicata, questa è sostituita (anche da un
    /// salvataggio manuale): la cronologia non accumula i salvataggi intermedi. Le revisioni manuali e pubblicate non si toccano.
    /// </summary>
    Task<EsitoSalvataggioBuilder> SalvaAsync(Guid idAttore, Guid idPagina, string? tokenConcorrenza, string? progettoEditorJson, bool automatico, CancellationToken annullamento);
}

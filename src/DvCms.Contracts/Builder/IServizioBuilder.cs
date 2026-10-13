using DvCms.DTO.Builder;

namespace DvCms.Contracts.Builder;

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

using DvCms.DTO.Localizzazione;
using DvCms.DTO.Piattaforma;

namespace DvCms.Contracts.Localizzazione;

/// <summary>
/// Gestione dei testi statici (B-010), riservata al <c>PlatformAdmin</c>: come per il menu "Platform", l'attore è
/// riverificato sul database a ogni operazione. Le letture non autorizzate lanciano <see cref="UnauthorizedAccessException"/>;
/// le scritture non autorizzate restituiscono <see cref="CodiceEsito.NonAutorizzato"/> e sono registrate nell'audit.
/// </summary>
public interface IServizioGestioneTesti
{
    public const int DimensionePagina = 50;

    Task<PaginaTesti> ElencaAsync(Guid idAttore, FiltroTesti filtro, CancellationToken annullamento);

    /// <summary>Salva il testo: azzera <c>DaTradurre</c>, aggiorna la cache locale e scrive nell'audit.</summary>
    Task<EsitoOperazione> ModificaAsync(Guid idAttore, Guid idTesto, string? testo, CancellationToken annullamento);
}

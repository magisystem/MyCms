using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;

namespace DvCms.Contracts.Negozio;

/// <summary>
/// Verifica di prontezza del negozio di un sito prima di accendere i pagamenti reali (ADR-0020, operazioni): credenziali PayPal e
/// webhook, interruttore di produzione, catalogo, spedizioni, canale email, pagine legali, manutenzione. Sola lettura, riservata al
/// <c>PlatformAdmin</c> (ruolo riverificato); non mostra mai i segreti.
/// </summary>
public interface IServizioProntezzaNegozio
{
    /// <summary>Il rapporto del sito, o <c>null</c> se il sito non è del tenant indicato. Lancia <see cref="UnauthorizedAccessException"/> a chi non è PlatformAdmin.</summary>
    Task<RapportoProntezza?> VerificaAsync(Guid idAttore, Guid idTenant, Guid idSito, CancellationToken annullamento);
}

using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;

namespace DvCms.Application.Negozio;

public sealed record DatiMetodoSpedizione(Guid? Id, TipoMetodoSpedizione Tipo, AreaSpedizione? Area, string? Nome, decimal TariffaLorda, bool Attivo);

public sealed record MetodoSpedizioneVista(Guid Id, TipoMetodoSpedizione Tipo, AreaSpedizione? Area, string Nome, decimal TariffaLorda, bool Attivo);

/// <summary>
/// Metodi di spedizione del sito (ADR-0020): tariffa fissa per area (Italia, resto dell'UE) e ritiro in sede, IVA inclusa. La
/// gestione è riservata agli amministratori del tenant (ruolo riverificato a ogni operazione) e al negozio attivo; l'elenco dei
/// metodi attivi lo vede ogni account che può ordinare.
/// </summary>
public interface IServizioSpedizioni
{
    /// <summary>Tutti i metodi del sito, anche disattivati (amministrazione).</summary>
    Task<IReadOnlyList<MetodoSpedizioneVista>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>Solo i metodi attivi, nell'ordine di presentazione (cassa).</summary>
    Task<IReadOnlyList<MetodoSpedizioneVista>> ElencaAttiviAsync(Guid idAccount, CancellationToken annullamento);

    /// <summary>Crea o modifica un metodo. <c>DatiNonValidi</c> (con i campi), <c>NonTrovato</c>, <c>NonConsentito</c> (negozio non attivo o troppi metodi).</summary>
    Task<EsitoOperazione> SalvaAsync(Guid idAttore, DatiMetodoSpedizione dati, CancellationToken annullamento);

    Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idMetodo, CancellationToken annullamento);
}

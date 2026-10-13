using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Negozio;

namespace DvCms.Contracts.Negozio;

/// <summary>
/// Catalogo del negozio del sito dell'host (F-001, F-006). Riservato agli amministratori del tenant (ruolo riverificato sul database a
/// ogni operazione); lavora solo sul tenant e sul sito dell'host e solo se il sito ha il negozio attivo (<c>CommercioAttivo</c>).
/// Gli importi sono <see cref="decimal"/> IVA inclusa in EUR (ADR-0020); il cliente non invia mai un prezzo che conti.
/// </summary>
public interface IServizioCatalogo
{
    /// <summary>Vero se il sito dell'host ha il negozio attivo.</summary>
    Task<bool> CommercioAttivoAsync(CancellationToken annullamento);

    /// <summary>Lingue abilitate del sito (la predefinita per prima): quelle in cui si scrivono nomi ed etichette.</summary>
    Task<IReadOnlyList<LinguaCatalogo>> LingueAsync(Guid idAttore, CancellationToken annullamento);

    Task<IReadOnlyList<RiepilogoProdotto>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    Task<DettaglioProdotto?> LeggiAsync(Guid idAttore, Guid idProdotto, CancellationToken annullamento);

    /// <summary>
    /// Crea o modifica un prodotto con le sue traduzioni e varianti, in una sola transazione. Esiti: <c>DatiNonValidi</c> (con i nomi
    /// dei campi), <c>GiaPresente</c> (codice articolo già usato nel sito), <c>Conflitto</c>, <c>NonConsentito</c> (negozio non attivo),
    /// <c>NonTrovato</c>, <c>NonAutorizzato</c>.
    /// </summary>
    Task<EsitoOperazione> SalvaAsync(Guid idAttore, DatiProdotto dati, CancellationToken annullamento);
}

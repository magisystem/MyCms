using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;

namespace DvCms.Application.Negozio;

/// <summary>Nome e descrizione di un prodotto in una lingua (testo semplice, sempre codificato in uscita).</summary>
public sealed record TraduzioneProdottoDati(string CodiceLingua, string Nome, string? Descrizione);

/// <summary>Una variante da salvare. Senza <see cref="Id"/> è nuova; il prezzo è IVA inclusa, con al più due decimali.</summary>
public sealed record VarianteDati(Guid? Id, string? Sku, decimal PrezzoLordo, string? Valuta, bool Attiva, IReadOnlyDictionary<string, string>? Etichette);

/// <summary>
/// Il prodotto da salvare. Senza <see cref="Id"/> si crea; con <see cref="Id"/> si modifica e <see cref="TokenConcorrenza"/> (quello letto
/// con il dettaglio) deve coincidere. Le varianti non elencate vengono tolte: si salva sempre lo stato completo.
/// </summary>
public sealed record DatiProdotto(
    Guid? Id,
    string? TokenConcorrenza,
    StatoProdotto Stato,
    decimal AliquotaIva,
    IReadOnlyList<TraduzioneProdottoDati> Traduzioni,
    IReadOnlyList<VarianteDati> Varianti);

public sealed record RiepilogoProdotto(Guid Id, string Nome, StatoProdotto Stato, int Varianti, decimal? PrezzoDa, string Valuta, DateTime AggiornatoIlUtc);

public sealed record VarianteDettaglio(Guid Id, string Sku, decimal PrezzoLordo, string Valuta, bool Attiva, IReadOnlyDictionary<string, string> Etichette);

public sealed record LinguaCatalogo(string Codice, string Nome, bool Predefinita);

public sealed record DettaglioProdotto(
    Guid Id,
    StatoProdotto Stato,
    decimal AliquotaIva,
    string TokenConcorrenza,
    IReadOnlyList<TraduzioneProdottoDati> Traduzioni,
    IReadOnlyList<VarianteDettaglio> Varianti);

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

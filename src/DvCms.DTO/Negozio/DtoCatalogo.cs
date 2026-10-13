using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

/// <summary>Nome e descrizione di un prodotto in una lingua (testo semplice, sempre codificato in uscita).</summary>
public sealed record TraduzioneProdottoDati(string CodiceLingua, string Nome, string? Descrizione);

/// <summary>Una variante da salvare. Senza <see cref="Id"/> è nuova; il prezzo è IVA inclusa, con al più due decimali.</summary>
public sealed record VarianteDati(Guid? Id, string? Sku, decimal PrezzoLordo, string? Valuta, bool Attiva, IReadOnlyDictionary<string, string>? Etichette);

/// <summary>
/// Il prodotto da salvare. <c>Immagini</c> sono risorse della libreria media (immagini pubbliche del sito) in ordine, la prima è la principale; se nullo non si toccano. Lo stesso vale per <c>Categorie</c> (categorie del sito a cui appartiene il prodotto). Senza <see cref="Id"/> si crea; con <see cref="Id"/> si modifica e <see cref="TokenConcorrenza"/> (quello letto
/// con il dettaglio) deve coincidere. Le varianti non elencate vengono tolte: si salva sempre lo stato completo.
/// </summary>
public sealed record DatiProdotto(
    Guid? Id,
    string? TokenConcorrenza,
    StatoProdotto Stato,
    decimal AliquotaIva,
    IReadOnlyList<TraduzioneProdottoDati> Traduzioni,
    IReadOnlyList<VarianteDati> Varianti,
    IReadOnlyList<Guid>? Immagini = null,
    IReadOnlyList<Guid>? Categorie = null);

public sealed record RiepilogoProdotto(Guid Id, string Nome, StatoProdotto Stato, int Varianti, decimal? PrezzoDa, string Valuta, DateTime AggiornatoIlUtc);

public sealed record VarianteDettaglio(Guid Id, string Sku, decimal PrezzoLordo, string Valuta, bool Attiva, IReadOnlyDictionary<string, string> Etichette);

public sealed record LinguaCatalogo(string Codice, string Nome, bool Predefinita);

public sealed record DettaglioProdotto(
    Guid Id,
    StatoProdotto Stato,
    decimal AliquotaIva,
    string TokenConcorrenza,
    IReadOnlyList<TraduzioneProdottoDati> Traduzioni,
    IReadOnlyList<VarianteDettaglio> Varianti,
    IReadOnlyList<Guid> Immagini,
    IReadOnlyList<Guid> Categorie);

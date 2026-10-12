namespace DvCms.Application.Negozio;

/// <summary>Un prodotto pubblicato nell'elenco della vetrina. <c>PrezzoDa</c> è il prezzo IVA inclusa più basso fra le varianti attive.</summary>
public sealed record ProdottoInVetrina(Guid Id, string Nome, decimal PrezzoDa, string Valuta, bool Disponibile, ImmagineVetrina? Immagine = null);

/// <summary>Un'immagine di prodotto servita da <c>/media/{id}</c>; <c>Testo</c> è il testo alternativo predefinito della risorsa, se c'è.</summary>
/// <summary>Una categoria della vetrina con almeno un prodotto visibile; <c>Prodotti</c> è quanti ne ha di visibili.</summary>
public sealed record CategoriaInVetrina(Guid Id, string Nome, int Prodotti);

public sealed record ImmagineVetrina(Guid Id, string? Testo);

/// <summary>
/// Una variante acquistabile. <c>Massimo</c> è la quantità che il cliente può chiedere adesso (al più 99): per giacenza gestita è
/// quanto non è già prenotato o venduto; zero significa esaurita. Non si espone la giacenza esatta.
/// </summary>
public sealed record VarianteInVetrina(Guid Id, string Sku, string? Etichetta, decimal PrezzoLordo, string Valuta, int Massimo);

public sealed record ProdottoPubblico(Guid Id, string Nome, string Descrizione, IReadOnlyList<VarianteInVetrina> Varianti, IReadOnlyList<ImmagineVetrina>? Immagini = null);

/// <summary>
/// Vetrina pubblica del negozio (F-001, ADR-0009): sola lettura, senza account. Mostra solo prodotti pubblicati con almeno una
/// variante attiva, solo se il sito ha il negozio attivo, e mai bozze, archiviati, varianti disattivate o dati di altri tenant.
/// </summary>
public interface IServizioVetrina
{
    /// <param name="codiceLingua">Lingua preferita; se manca la traduzione si ripiega sulla lingua predefinita del sito.</param>
    Task<IReadOnlyList<ProdottoInVetrina>> ElencaAsync(string codiceLingua, CancellationToken annullamento);

    /// <summary>Come <see cref="ElencaAsync"/>, ma solo i prodotti della categoria. Vuoto se la categoria non esiste o non ha prodotti visibili.</summary>
    Task<IReadOnlyList<ProdottoInVetrina>> ElencaPerCategoriaAsync(string codiceLingua, Guid idCategoria, CancellationToken annullamento);

    /// <summary>Le categorie del sito che hanno almeno un prodotto visibile, nell'ordine di presentazione. Nome nella lingua richiesta, con ripiego sulla predefinita.</summary>
    Task<IReadOnlyList<CategoriaInVetrina>> CategorieAsync(string codiceLingua, CancellationToken annullamento);

    /// <summary><c>null</c> se il prodotto non esiste, non è pubblicato o il negozio non è attivo.</summary>
    Task<ProdottoPubblico?> LeggiAsync(Guid idProdotto, string codiceLingua, CancellationToken annullamento);
}

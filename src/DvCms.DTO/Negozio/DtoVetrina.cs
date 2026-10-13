namespace DvCms.DTO.Negozio;

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

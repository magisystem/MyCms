
namespace DvCms.DTO.Negozio;

public sealed record NomeCategoriaDati(string CodiceLingua, string Nome);

public sealed record DatiCategoria(Guid? Id, int Posizione, IReadOnlyList<NomeCategoriaDati> Nomi);

public sealed record CategoriaVista(Guid Id, int Posizione, IReadOnlyList<NomeCategoriaDati> Nomi, int Prodotti);

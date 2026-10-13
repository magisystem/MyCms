
namespace DvCms.DTO.Localizzazione;

public sealed record FiltroTesti(string? Ricerca, string? CodiceLingua, bool SoloDaTradurre, int Pagina);

public sealed record TestoGestito(
    Guid Id,
    string CodiceLingua,
    string Chiave,
    string Testo,
    bool DaTradurre,
    DateTime AggiornatoIlUtc);

public sealed record PaginaTesti(IReadOnlyList<TestoGestito> Testi, int Pagina, int TotalePagine, int Totale);

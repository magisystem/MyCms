namespace DvCms.DTO.Cms;

/// <summary>Un'altra versione linguistica di una voce, per i collegamenti <c>xhtml:link</c> della sitemap.</summary>
public sealed record AlternativaSitemap(string CodiceLingua, string Percorso);

/// <summary>Un indirizzo canonico indicizzabile del sito. Il percorso è relativo: il dominio lo mette chi risponde, dall'host richiesto.</summary>
public sealed record VoceSitemap(string Percorso, DateTime ModificatoIlUtc, IReadOnlyList<AlternativaSitemap> Alternative, string? PercorsoPredefinito);

public sealed record SitemapSito(IReadOnlyList<VoceSitemap> Voci, bool Troncata);

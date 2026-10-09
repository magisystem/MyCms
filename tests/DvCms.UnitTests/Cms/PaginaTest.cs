using DvCms.Domain.Cms;
using Xunit;

namespace DvCms.UnitTests.Cms;

public class PaginaTest
{
    private static readonly Guid _tenant = Guid.NewGuid();
    private static readonly Guid _sito = Guid.NewGuid();
    private static readonly Guid _autore = Guid.NewGuid();

    private static (Pagina Pagina, RevisionePagina Revisione) Nuova(TimeProvider? orologio = null) =>
        Pagina.Crea(_tenant, _sito, Guid.NewGuid(), "Chi-Siamo", "Chi siamo", "Testo", paginaIniziale: false, _autore, orologio ?? TimeProvider.System);

    [Fact]
    public void LaCreazioneProduceUnaBozzaConLaPrimaRevisione()
    {
        var (pagina, revisione) = Nuova();

        Assert.Equal("chi-siamo", pagina.Slug);
        Assert.Equal(StatoPagina.Bozza, pagina.Stato);
        Assert.Null(pagina.IdRevisionePubblicata);
        Assert.Equal(revisione.Id, pagina.IdUltimaRevisioneBozza);
        Assert.Equal(1, revisione.NumeroRevisione);
        Assert.Equal(1, pagina.UltimoNumeroRevisione);
        Assert.Equal(pagina.Id, revisione.IdPagina);
        Assert.Equal(_tenant, revisione.IdTenant);
        Assert.Equal(_sito, revisione.IdSito);
        Assert.Equal(_autore, revisione.CreatoDaIdUtenza);
        Assert.NotEqual(Guid.Empty, pagina.IdGruppoTraduzione);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("non valido")]
    public void LaCreazioneRifiutaSlugRiservatiONonValidi(string slug)
    {
        Assert.Throws<ArgumentException>(() =>
            Pagina.Crea(_tenant, _sito, Guid.NewGuid(), slug, "T", "C", false, _autore, TimeProvider.System));
    }

    [Fact]
    public void OgniModificaCreaUnaNuovaRevisioneENonToccaLeAltre()
    {
        var (pagina, prima) = Nuova();
        var contenutoPrima = prima.Contenuto;

        var seconda = pagina.AggiungiRevisione("Nuovo titolo", "Nuovo testo", "ritocco", _autore, TimeProvider.System);

        Assert.Equal(2, seconda.NumeroRevisione);
        Assert.Equal(seconda.Id, pagina.IdUltimaRevisioneBozza);
        Assert.Equal(contenutoPrima, prima.Contenuto);
        Assert.Equal("ritocco", seconda.NotaRevisione);
    }

    [Fact]
    public void BozzaEPubblicatoSonoSeparati()
    {
        var (pagina, prima) = Nuova();
        pagina.Pubblica(prima, TimeProvider.System);
        var seconda = pagina.AggiungiRevisione("T2", "C2", null, _autore, TimeProvider.System);

        Assert.Equal(StatoPagina.Pubblicata, pagina.Stato);
        Assert.Equal(prima.Id, pagina.IdRevisionePubblicata);
        Assert.Equal(seconda.Id, pagina.IdUltimaRevisioneBozza);
        Assert.NotNull(pagina.PubblicatoIlUtc);
    }

    [Fact]
    public void RitirareTogliLaPaginaDalPubblicoMaTieneLeRevisioni()
    {
        var (pagina, prima) = Nuova();
        pagina.Pubblica(prima, TimeProvider.System);

        pagina.Ritira(TimeProvider.System);

        Assert.Equal(StatoPagina.Bozza, pagina.Stato);
        Assert.Null(pagina.IdRevisionePubblicata);
        Assert.Null(pagina.PubblicatoIlUtc);
        Assert.Equal(prima.Id, pagina.IdUltimaRevisioneBozza);
    }

    [Fact]
    public void NonSiPubblicaLaRevisioneDiUnAltraPagina()
    {
        var (pagina, _) = Nuova();
        var (_, estranea) = Nuova();

        Assert.Throws<ArgumentException>(() => pagina.Pubblica(estranea, TimeProvider.System));
    }

    [Theory]
    [InlineData("", "testo")]
    [InlineData("   ", "testo")]
    public void TitoloVuotoRifiutato(string titolo, string contenuto)
    {
        Assert.False(RevisionePagina.TentaValidaTesto(titolo, contenuto, out _, out _));
    }

    [Fact]
    public void ILimitiDiLunghezzaEIControlliSonoApplicati()
    {
        Assert.True(RevisionePagina.TentaValidaTesto(new string('t', RevisionePagina.LunghezzaMassimaTitolo), new string('c', RevisionePagina.LunghezzaMassimaContenuto), out _, out _));
        Assert.False(RevisionePagina.TentaValidaTesto(new string('t', RevisionePagina.LunghezzaMassimaTitolo + 1), "c", out _, out _));
        Assert.False(RevisionePagina.TentaValidaTesto("t", new string('c', RevisionePagina.LunghezzaMassimaContenuto + 1), out _, out _));
        Assert.False(RevisionePagina.TentaValidaTesto("t\nitolo", "c", out _, out _));
        Assert.False(RevisionePagina.TentaValidaTesto("t", "c\u0000", out _, out _));
        Assert.True(RevisionePagina.TentaValidaTesto("t", "riga uno\r\n\r\nriga due\tcon tab", out _, out var normalizzato));
        Assert.DoesNotContain('\r', normalizzato);
    }

    [Fact]
    public void LaPubblicazioneRicordaChelaPaginaEStataPubblicataAncheDopoIlRitiro()
    {
        var (pagina, revisione) = Nuova();
        Assert.False(pagina.PubblicataInPassato);

        pagina.Pubblica(revisione, TimeProvider.System);
        pagina.Ritira(TimeProvider.System);

        Assert.True(pagina.PubblicataInPassato);
        Assert.Equal(StatoPagina.Bozza, pagina.Stato);
    }

    [Fact]
    public void ICampiSeoSonoNormalizzatiELimitati()
    {
        Assert.True(RevisionePagina.TentaValidaSeo("  Titolo  ", "   ", out var titolo, out var descrizione));
        Assert.Equal("Titolo", titolo);
        Assert.Null(descrizione);
        Assert.True(RevisionePagina.TentaValidaSeo(new string('t', RevisionePagina.LunghezzaMassimaTitoloSeo), new string('d', RevisionePagina.LunghezzaMassimaDescrizioneSeo), out _, out _));
        Assert.False(RevisionePagina.TentaValidaSeo(new string('t', RevisionePagina.LunghezzaMassimaTitoloSeo + 1), null, out _, out _));
        Assert.False(RevisionePagina.TentaValidaSeo(null, new string('d', RevisionePagina.LunghezzaMassimaDescrizioneSeo + 1), out _, out _));
        Assert.False(RevisionePagina.TentaValidaSeo("a\nb", null, out _, out _));
        Assert.False(RevisionePagina.TentaValidaSeo(null, "a\u0000b", out _, out _));
    }

    [Fact]
    public void LaRevisioneConservaICampiSeo()
    {
        var (pagina, _) = Nuova();

        var revisione = pagina.AggiungiRevisione(new ContenutoRevisione("T", "C", TitoloSeo: " Seo ", DescrizioneSeo: "Descr", NonIndicizzare: true), null, _autore, TimeProvider.System);

        Assert.Equal("Seo", revisione.TitoloSeo);
        Assert.Equal("Descr", revisione.DescrizioneSeo);
        Assert.True(revisione.NonIndicizzare);
        Assert.Throws<ArgumentException>(() =>
            pagina.AggiungiRevisione(new ContenutoRevisione("T", "C", TitoloSeo: new string('t', 201)), null, _autore, TimeProvider.System));
    }

    [Fact]
    public void UnReindirizzamentoNonPuoPuntareAlloStessoSlugEPuntaAllaPagina()
    {
        var (pagina, _) = Nuova();

        var redirect = RedirectPagina.Crea(pagina, "Vecchio-Slug", TimeProvider.System);

        Assert.Equal("vecchio-slug", redirect.SlugOrigine);
        Assert.Equal(pagina.Id, redirect.IdPagina);
        Assert.Equal(pagina.IdLingua, redirect.IdLingua);
        Assert.Equal(pagina.IdSito, redirect.IdSito);
        Assert.Equal(pagina.IdTenant, redirect.IdTenant);
        Assert.Throws<ArgumentException>(() => RedirectPagina.Crea(pagina, "chi-siamo", TimeProvider.System));
        Assert.Throws<ArgumentException>(() => RedirectPagina.Crea(pagina, "admin", TimeProvider.System));
    }
}

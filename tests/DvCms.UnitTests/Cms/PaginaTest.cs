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
}

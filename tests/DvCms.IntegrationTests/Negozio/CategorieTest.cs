using System.Net;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Categorie del negozio: gestione riservata, validazione, assegnazione ai prodotti, isolamento fra tenant e filtro in vetrina.</summary>
public class CategorieTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<EsitoOperazione> CreaCategoriaAsync(ContestoNegozio c, string nome, int posizione = 0, DatiProva.TenantCreato? tenant = null) =>
        c.ComeCategorieAsync(tenant ?? c.A, s => s.SalvaAsync(c.Attore(tenant ?? c.A), new DatiCategoria(null, posizione, [new NomeCategoriaDati("it", nome)]), Ct));

    private static Task<EsitoOperazione> AssegnaAsync(ContestoNegozio c, DettaglioProdotto d, IReadOnlyList<Guid>? categorie, DatiProva.TenantCreato? tenant = null) =>
        c.ComeCatalogoAsync(tenant ?? c.A, s => s.SalvaAsync(
            c.Attore(tenant ?? c.A),
            new DatiProdotto(d.Id, d.TokenConcorrenza, d.Stato, d.AliquotaIva, d.Traduzioni, [.. d.Varianti.Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))], null, categorie),
            Ct));

    private static async Task<DettaglioProdotto> ProdottoAsync(ContestoNegozio c, string nome, string sku, DatiProva.TenantCreato? tenant = null)
    {
        var varianti = await c.CreaProdottoAsync(tenant ?? c.A, nome, 22m, (sku, 10m, null));
        var id = await c.IdProdottoAsync(varianti[0]);
        return (await c.ComeCatalogoAsync(tenant ?? c.A, s => s.LeggiAsync(c.Attore(tenant ?? c.A), id, Ct)))!;
    }

    [Fact]
    public async Task SoloGliAmministratoriDelTenantGestisconoLeCategorieEIlNomeInItalianoServe()
    {
        await using var c = await ContestoNegozio.CreaAsync();

        var ok = await CreaCategoriaAsync(c, "Libri", 2);
        var comeCliente = await c.ComeCategorieAsync(c.A, s => s.SalvaAsync(c.Cliente, new DatiCategoria(null, 0, [new NomeCategoriaDati("it", "X")]), Ct));
        var comeAltroTenant = await c.ComeCategorieAsync(c.A, s => s.SalvaAsync(c.AdminB, new DatiCategoria(null, 0, [new NomeCategoriaDati("it", "X")]), Ct));
        var senzaNome = await c.ComeCategorieAsync(c.A, s => s.SalvaAsync(c.AdminA, new DatiCategoria(null, 0, [new NomeCategoriaDati("it", "  ")]), Ct));
        var linguaIgnota = await c.ComeCategorieAsync(c.A, s => s.SalvaAsync(c.AdminA, new DatiCategoria(null, 0, [new NomeCategoriaDati("it", "Y"), new NomeCategoriaDati("xx", "Z")]), Ct));
        var posizione = await c.ComeCategorieAsync(c.A, s => s.SalvaAsync(c.AdminA, new DatiCategoria(null, -1, [new NomeCategoriaDati("it", "Y")]), Ct));

        Assert.True(ok.Riuscito);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeCliente.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAltroTenant.Codice);
        Assert.Equal((CodiceEsito.DatiNonValidi, "Nome"), (senzaNome.Codice, Assert.Single(senzaNome.CampiNonValidi!)));
        Assert.Contains("Nomi", linguaIgnota.CampiNonValidi!);
        Assert.Contains("Posizione", posizione.CampiNonValidi!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeCategorieAsync(c.A, s => s.ElencaAsync(c.Cliente, Ct)));
        Assert.Empty(await c.ComeCategorieAsync(c.B, s => s.ElencaAsync(c.AdminB, Ct)));
    }

    [Fact]
    public async Task ASiAssegnanoAiProdottiSiModificanoEEliminandoUnaCategoriaIProdottiRestano()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var libri = (await CreaCategoriaAsync(c, "Libri", 1)).Id!.Value;
        var penne = (await CreaCategoriaAsync(c, "Penne", 2)).Id!.Value;
        var prodotto = await ProdottoAsync(c, "Quaderno", "Q-1");

        Assert.True((await AssegnaAsync(c, prodotto, [libri, penne])).Riuscito);
        var assegnato = (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, prodotto.Id, Ct)))!;
        Assert.Equal(new[] { libri, penne }.Order(), assegnato.Categorie.Order());

        var elenco = await c.ComeCategorieAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct));
        Assert.Equal([("Libri", 1), ("Penne", 1)], elenco.Select(x => (x.Nomi[0].Nome, x.Prodotti)));

        Assert.True((await AssegnaAsync(c, assegnato, null)).Riuscito);
        Assert.Equal(2, (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, prodotto.Id, Ct)))!.Categorie.Count);

        assegnato = (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, prodotto.Id, Ct)))!;
        Assert.True((await c.ComeCategorieAsync(c.A, s => s.EliminaAsync(c.AdminA, libri, Ct))).Riuscito);
        var dopo = (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, prodotto.Id, Ct)))!;
        Assert.Equal([penne], dopo.Categorie);
        Assert.Equal(CodiceEsito.NonTrovato, (await c.ComeCategorieAsync(c.A, s => s.EliminaAsync(c.AdminA, libri, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeCategorieAsync(c.A, s => s.EliminaAsync(c.Cliente, penne, Ct))).Codice);
    }

    [Fact]
    public async Task UnProdottoNonPuoEssereAssegnatoACategorieInesistentiRipetuteODiAltriTenant()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var mia = (await CreaCategoriaAsync(c, "Mia")).Id!.Value;
        var diB = (await CreaCategoriaAsync(c, "Di B", 0, c.B)).Id!.Value;
        var prodotto = await ProdottoAsync(c, "Quaderno", "Q-1");

        foreach (var cattive in new List<Guid>[] { [Guid.NewGuid()], [mia, mia], [diB] })
        {
            var esito = await AssegnaAsync(c, prodotto, cattive);
            Assert.Equal((CodiceEsito.DatiNonValidi, "Categorie"), (esito.Codice, Assert.Single(esito.CampiNonValidi!)));
        }

        Assert.Empty((await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, prodotto.Id, Ct)))!.Categorie);
        Assert.Equal(CodiceEsito.NonTrovato, (await c.ComeCategorieAsync(c.A, s => s.EliminaAsync(c.AdminA, diB, Ct))).Codice);
    }

    [Fact]
    public async Task LaVetrinaFiltraPerCategoriaMostraSoloQuelleConProdottiVisibiliENonConfondeITenant()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var libri = (await CreaCategoriaAsync(c, "Libri", 1)).Id!.Value;
        var vuota = (await CreaCategoriaAsync(c, "Vuota", 2)).Id!.Value;
        var soloBozza = (await CreaCategoriaAsync(c, "Solo bozze", 3)).Id!.Value;
        var diB = (await CreaCategoriaAsync(c, "Di B", 0, c.B)).Id!.Value;
        var romanzo = await ProdottoAsync(c, "Romanzo", "R-1");
        var penna = await ProdottoAsync(c, "Penna", "P-1");
        var segreto = await c.CreaProdottoAsync(c.A, "Segreto", 22m, StatoProdotto.Bozza, ("S-1", 5m, null));
        var idSegreto = await c.IdProdottoAsync(segreto[0]);
        var segretoDettaglio = (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, idSegreto, Ct)))!;
        Assert.True((await AssegnaAsync(c, romanzo, [libri])).Riuscito);
        Assert.True((await AssegnaAsync(c, segretoDettaglio, [soloBozza])).Riuscito);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        var tutti = await (await anonimo.GetAsync("/negozio")).Content.ReadAsStringAsync(Ct);
        var filtrata = await (await anonimo.GetAsync($"/negozio?categoria={libri}")).Content.ReadAsStringAsync(Ct);
        using var categoriaVuota = await anonimo.GetAsync($"/negozio?categoria={vuota}");
        using var categoriaSoloBozze = await anonimo.GetAsync($"/negozio?categoria={soloBozza}");
        using var categoriaDiB = await anonimo.GetAsync($"/negozio?categoria={diB}");
        using var inesistente = await anonimo.GetAsync($"/negozio?categoria={Guid.NewGuid()}");

        Assert.Contains("Romanzo", tutti, StringComparison.Ordinal);
        Assert.Contains("Penna", tutti, StringComparison.Ordinal);
        Assert.Contains($"/negozio?categoria={libri}", tutti, StringComparison.Ordinal);
        Assert.DoesNotContain("Vuota", tutti, StringComparison.Ordinal);
        Assert.DoesNotContain("Solo bozze", tutti, StringComparison.Ordinal);
        Assert.DoesNotContain("Di B", tutti, StringComparison.Ordinal);
        Assert.Contains("Romanzo", filtrata, StringComparison.Ordinal);
        Assert.DoesNotContain("Penna", filtrata, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, categoriaVuota.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, categoriaSoloBozze.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, categoriaDiB.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inesistente.StatusCode);
        Assert.NotNull(penna);
    }

    [Fact]
    public async Task LePagineAdminPermettonoDiCreareAssegnareEMostranoLaCategoriaInVetrina()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var prodotto = await ProdottoAsync(c, "Quaderno", "Q-1");

        using var crea = await anna.InviaModuloAsync("/admin/negozio/categorie", [new("Nomi[it]", "Cancelleria <b>fine</b>"), new("Posizione", "1")], "/admin/negozio/categorie?handler=Salva");
        var elenco = await (await anna.GetAsync("/admin/negozio/categorie")).Content.ReadAsStringAsync(Ct);
        var idCategoria = (await c.ComeCategorieAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct))).Single().Id;
        var modulo = await (await anna.GetAsync($"/admin/negozio/prodotti/{prodotto.Id}")).Content.ReadAsStringAsync(Ct);
        using var assegna = await anna.InviaModuloAsync(
            $"/admin/negozio/prodotti/{prodotto.Id}",
            [
                new("Modulo.Token", prodotto.TokenConcorrenza),
                new("Modulo.Stato", "Pubblicato"),
                new("Modulo.Aliquota", "22"),
                new("Modulo.Traduzioni[0].Codice", "it"),
                new("Modulo.Traduzioni[0].Nome", "Quaderno"),
                new("Modulo.Varianti[0].Id", prodotto.Varianti[0].Id.ToString()),
                new("Modulo.Varianti[0].Sku", "Q-1"),
                new("Modulo.Varianti[0].Prezzo", "10,00"),
                new("Modulo.Varianti[0].Attiva", "true"),
                new("Modulo.Categorie", idCategoria.ToString()),
            ]);
        var vetrina = await (await c.Web.SessioneSu(c.A.Host).GetAsync("/negozio")).Content.ReadAsStringAsync(Ct);
        using var comeCliente = await c.Web.SessioneSu(c.A.Host).GetAsync("/admin/negozio/categorie");

        Assert.True(SessioneProva.EReindirizzamento(crea), await crea.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Cancelleria &lt;b&gt;fine&lt;/b&gt;", elenco, StringComparison.Ordinal);
        Assert.Contains($"value=\"{idCategoria}\"", modulo, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(assegna), await assegna.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Cancelleria &lt;b&gt;fine&lt;/b&gt; (1)", vetrina, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>fine", vetrina, StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, comeCliente.StatusCode);
    }
}

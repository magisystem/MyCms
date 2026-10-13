using System.Net;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Media;
using DvCms.Models.Negozio;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Immagini dei prodotti: ordine, validazione, isolamento fra tenant e visibilità in vetrina.</summary>
public class ImmaginiProdottoTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Guid Id, DettaglioProdotto Dettaglio)> CreaAsync(ContestoNegozio c, params Guid[] immagini)
    {
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        var id = await c.IdProdottoAsync(varianti[0]);
        var dettaglio = (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, id, Ct)))!;
        if (immagini.Length > 0)
        {
            dettaglio = await SalvaAsync(c, dettaglio, immagini);
        }

        return (id, dettaglio);
    }

    private static async Task<DettaglioProdotto> SalvaAsync(ContestoNegozio c, DettaglioProdotto dettaglio, IReadOnlyList<Guid>? immagini)
    {
        var esito = await EsitoAsync(c, dettaglio, immagini);
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        return (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, dettaglio.Id, Ct)))!;
    }

    private static Task<EsitoOperazione> EsitoAsync(ContestoNegozio c, DettaglioProdotto dettaglio, IReadOnlyList<Guid>? immagini) =>
        c.ComeCatalogoAsync(c.A, s => s.SalvaAsync(
            c.AdminA,
            new DatiProdotto(
                dettaglio.Id,
                dettaglio.TokenConcorrenza,
                dettaglio.Stato,
                dettaglio.AliquotaIva,
                dettaglio.Traduzioni,
                [.. dettaglio.Varianti.Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))],
                immagini),
            Ct));

    [Fact]
    public async Task LeImmaginiSiSalvanoInOrdineSiRiordinanoENulloLeLasciaCosiCome()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var uno = await c.CaricaImmagineAsync(c.A);
        var due = await c.CaricaImmagineAsync(c.A);
        var (_, dettaglio) = await CreaAsync(c, uno, due);
        Assert.Equal([uno, due], dettaglio.Immagini);

        dettaglio = await SalvaAsync(c, dettaglio, [due, uno]);
        Assert.Equal([due, uno], dettaglio.Immagini);

        dettaglio = await SalvaAsync(c, dettaglio, null);
        Assert.Equal([due, uno], dettaglio.Immagini);

        dettaglio = await SalvaAsync(c, dettaglio, [uno]);
        Assert.Equal([uno], dettaglio.Immagini);

        dettaglio = await SalvaAsync(c, dettaglio, []);
        Assert.Empty(dettaglio.Immagini);
    }

    [Fact]
    public async Task ImmaginiRipetuteTroppeInesistentiPrivateOdiAltriTenantSiRifiutano()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var buona = await c.CaricaImmagineAsync(c.A);
        var privata = await c.CaricaImmagineAsync(c.A, VisibilitaMedia.Privata);
        var diB = await c.CaricaImmagineAsync(c.B);
        var (_, dettaglio) = await CreaAsync(c);
        var nove = new List<Guid>();
        for (var i = 0; i < ImmagineProdotto.NumeroMassimo + 1; i++)
        {
            nove.Add(await c.CaricaImmagineAsync(c.A));
        }

        foreach (var cattive in new List<Guid>[] { [buona, buona], [Guid.NewGuid()], [privata], [diB], nove })
        {
            var esito = await EsitoAsync(c, dettaglio, cattive);
            Assert.Equal((CodiceEsito.DatiNonValidi, "Immagini"), (esito.Codice, Assert.Single(esito.CampiNonValidi!)));
        }

        Assert.Empty((await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, dettaglio.Id, Ct)))!.Immagini);
    }

    [Fact]
    public async Task LaVetrinaMostraLePrincipaliELeImmaginiPubblicheEUnaRisorsaToltaSparisceSenzaRompereIlProdotto()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var prima = await c.CaricaImmagineAsync(c.A);
        var seconda = await c.CaricaImmagineAsync(c.A);
        var (id, dettaglio) = await CreaAsync(c, prima, seconda);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        var elenco = await (await anonimo.GetAsync("/negozio")).Content.ReadAsStringAsync(Ct);
        var scheda = await (await anonimo.GetAsync($"/negozio/{id}")).Content.ReadAsStringAsync(Ct);
        Assert.Contains($"src=\"/media/{prima}\"", elenco, StringComparison.Ordinal);
        Assert.DoesNotContain($"/media/{seconda}", elenco, StringComparison.Ordinal);
        Assert.Contains($"src=\"/media/{prima}\"", scheda, StringComparison.Ordinal);
        Assert.Contains($"src=\"/media/{seconda}\"", scheda, StringComparison.Ordinal);

        Assert.True((await c.EliminaMediaAsync(c.A, prima)).Riuscito);
        using var dopoElimina = await anonimo.GetAsync($"/negozio/{id}");
        var htmlDopo = await dopoElimina.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, dopoElimina.StatusCode);
        Assert.DoesNotContain($"/media/{prima}", htmlDopo, StringComparison.Ordinal);
        Assert.Contains($"src=\"/media/{seconda}\"", htmlDopo, StringComparison.Ordinal);
        var elencoDopo = await (await anonimo.GetAsync("/negozio")).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain($"/media/{prima}", elencoDopo, StringComparison.Ordinal);
        Assert.Contains($"src=\"/media/{seconda}\"", elencoDopo, StringComparison.Ordinal);
        Assert.NotEmpty(dettaglio.Varianti);
    }

    [Fact]
    public async Task IlModuloAdminMostraLeImmaginiELeSalvaConLaPosizione()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var uno = await c.CaricaImmagineAsync(c.A);
        var due = await c.CaricaImmagineAsync(c.A);
        var (id, dettaglio) = await CreaAsync(c);
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var percorso = $"/admin/negozio/prodotti/{id}";
        var variante = dettaglio.Varianti[0];

        using var modulo = await anna.GetAsync(percorso);
        var html = await modulo.Content.ReadAsStringAsync(Ct);
        using var salvato = await anna.InviaModuloAsync(
            percorso,
            [
                new("Modulo.Token", dettaglio.TokenConcorrenza),
                new("Modulo.Stato", "Pubblicato"),
                new("Modulo.Aliquota", "22"),
                new("Modulo.Traduzioni[0].Codice", "it"),
                new("Modulo.Traduzioni[0].Nome", "Libro"),
                new("Modulo.Varianti[0].Id", variante.Id.ToString()),
                new("Modulo.Varianti[0].Sku", variante.Sku),
                new("Modulo.Varianti[0].Prezzo", "10,00"),
                new("Modulo.Varianti[0].Attiva", "true"),
                new($"Modulo.Immagini[{uno}]", "2"),
                new($"Modulo.Immagini[{due}]", "1"),
            ]);

        Assert.Contains($"name=\"Modulo.Immagini[{uno}]\"", html, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(salvato), await salvato.Content.ReadAsStringAsync(Ct));
        Assert.Equal([due, uno], (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, id, Ct)))!.Immagini);
    }
}

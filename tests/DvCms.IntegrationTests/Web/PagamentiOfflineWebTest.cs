using System.Net;
using DvCms.Application.Negozio;
using DvCms.Domain.Negozio;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Bonifico e contrassegno dal sito (ADR-0023): impostazioni del TenantAdmin, cassa del cliente, pagina dell'ordine e azioni in amministrazione.</summary>
public class PagamentiOfflineWebTest
{
    private const string Percorso = "/admin/negozio/pagamenti";
    private const string PercorsoSalva = Percorso + "?handler=Salva";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<KeyValuePair<string, string>> Modulo(
        bool bonifico = true,
        bool contrassegno = true,
        string iban = "IT60 X054 2811 1010 0000 0123 456",
        string fissa = "4,50",
        string percentuale = "0",
        string istruzioni = "Indica l'ordine nella causale.") =>
    [
        new("BonificoAttivo", bonifico ? "true" : "false"),
        new("Intestatario", "Negozio Srl"),
        new("Iban", iban),
        new("Bic", "BCITITMM"),
        new("NomeBanca", "Banca di Prova"),
        new("GiorniPerPagare", "10"),
        new("IstruzioniBonifico", istruzioni),
        new("ContrassegnoAttivo", contrassegno ? "true" : "false"),
        new("CommissioneFissa", fissa),
        new("CommissionePercentuale", percentuale),
        new("CommissioneGratisSopra", string.Empty),
        new("OrdineMinimo", string.Empty),
        new("OrdineMassimo", string.Empty),
        new("IstruzioniContrassegno", "Paga al corriere."),
    ];

    private static async Task<SessioneProva> AdminAsync(ContestoNegozio c, string email = "anna@esempio.it")
    {
        var sessione = c.Web.SessioneSu(c.A.Host);
        await sessione.AccediAsync(email, DatiProva.PasswordProva);
        return sessione;
    }

    private static async Task<SessioneProva> ClienteAsync(ContestoNegozio c)
    {
        var sessione = c.Web.SessioneSu(c.A.Host);
        using var accesso = await sessione.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));
        return sessione;
    }

    private static async Task<(ContestoNegozio C, Guid Italia, Guid Ritiro)> PreparaAsync(bool configura = true)
    {
        var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 22m, ("LIB-1", 20.00m, null));
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        var italia = await c.CreaMetodoAsync(c.A, "Corriere Italia", 5.00m, AreaSpedizione.Italia);
        var ritiro = await c.CreaRitiroAsync(c.A);
        if (configura)
        {
            await c.ConfiguraOfflineAsync(c.A, ContestoNegozio.ImpostazioniOffline with { CommissioneFissa = 4.50m });
        }

        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        return (c, italia, ritiro);
    }

    private static Guid IdDa(HttpResponseMessage reindirizzamento)
    {
        var destinazione = SessioneProva.Destinazione(reindirizzamento)!;
        return Guid.Parse(destinazione[(destinazione.LastIndexOf('/') + 1)..]);
    }

    private static List<KeyValuePair<string, string>> Cassa(Guid metodo, string modalita, string? atteso)
    {
        var campi = new List<KeyValuePair<string, string>>
        {
            new("Modulo.IdMetodo", metodo.ToString()),
            new("Modulo.Modalita", modalita),
            new("Modulo.Destinatario", "Mario Rossi"),
            new("Modulo.IndirizzoRiga1", "Via Roma 1"),
            new("Modulo.Cap", "20100"),
            new("Modulo.Citta", "Milano"),
            new("Modulo.Paese", "IT"),
        };
        if (atteso is not null)
        {
            campi.Add(new($"Modulo.Atteso[{metodo}:{modalita}]", atteso));
        }

        return campi;
    }

    [Fact]
    public async Task ILinkEMenuCompaionoSoloConIlNegozioAttivoELaPaginaEDelTenantAdmin()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = await AdminAsync(c);
        var cliente = await ClienteAsync(c);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var cruscotto = await anna.GetAsync("/admin");
        using var pagina = await anna.GetAsync(Percorso);
        using var comeCliente = await cliente.GetAsync(Percorso);
        using var comeAnonimo = await anonimo.GetAsync(Percorso);

        Assert.Contains(Percorso, await cruscotto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, comeCliente.StatusCode);
        Assert.True(SessioneProva.EReindirizzamento(comeAnonimo) || comeAnonimo.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        await c.SpegniNegozioAsync(c.A);
        using var spento = await anna.GetAsync(Percorso);
        Assert.Equal(HttpStatusCode.NotFound, spento.StatusCode);
    }

    [Fact]
    public async Task IlTenantAdminAttivaBonificoEContrassegnoConLaVirgolaEIPuoRivedere()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = await AdminAsync(c);
        using var prima = await anna.GetAsync(Percorso);

        using var salva = await anna.InviaModuloAsync(Percorso, Modulo(fissa: "4,50", percentuale: "1,5"), PercorsoSalva);
        using var dopo = await anna.GetAsync(Percorso);

        Assert.True(SessioneProva.EReindirizzamento(salva), await salva.Content.ReadAsStringAsync(Ct));
        var salvate = (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Impostazioni;
        Assert.Equal((true, ContestoNegozio.IbanProva, 10, true, 4.50m, 1.5m), (salvate.BonificoAttivo, salvate.Iban, salvate.GiorniPerPagare, salvate.ContrassegnoAttivo, salvate.CommissioneFissa, salvate.CommissionePercentuale));
        var html = WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct));
        Assert.Contains("value=\"IT60 X054 2811 1010 0000 0123 456\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"4.50\"", html, StringComparison.Ordinal);
        Assert.Contains("Pagamenti salvati", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IT60X0542811101000000123457", "4,50", "Iban")]
    [InlineData("", "4,50", "Iban")]
    [InlineData("IT60X0542811101000000123456", "molto", "CommissioneFissa")]
    [InlineData("IT60X0542811101000000123456", "-1", "CommissioneFissa")]
    public async Task UnDatoNonValidoNonSalvaENonPerdeIlTestoScritto(string iban, string fissa, string campo)
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = await AdminAsync(c);

        using var risposta = await anna.InviaModuloAsync(Percorso, Modulo(iban: iban, fissa: fissa), PercorsoSalva);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var html = WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct));
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("is-invalid", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{fissa}\"", html, StringComparison.Ordinal);
        Assert.Equal(ImpostazioniPagamentoOffline.Spente, (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Impostazioni);
        Assert.NotNull(campo);
    }

    [Fact]
    public async Task IlTestoDelleIstruzioniSiCodificaEUnClienteNonPuoSalvare()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = await AdminAsync(c);
        var cliente = await ClienteAsync(c);
        using var salva = await anna.InviaModuloAsync(Percorso, Modulo(istruzioni: "<script>alert(1)</script>"), PercorsoSalva);

        using var pagina = await anna.GetAsync(Percorso);
        using var nonConsentita = await cliente.InviaModuloAsync(Percorso, Modulo(fissa: "0,01"), PercorsoSalva);

        var html = await pagina.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, nonConsentita.StatusCode);
        Assert.Equal(4.50m, (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Impostazioni.CommissioneFissa);
    }

    [Fact]
    public async Task LeImpostazioniDiUnTenantNonSiVedonoNeSiSovrascrivonoDaUnAltro()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.ConfiguraOfflineAsync(c.B, ContestoNegozio.ImpostazioniOffline with { Intestatario = "Segreto di B" });
        var anna = await AdminAsync(c);

        using var pagina = await anna.GetAsync(Percorso);
        using var salva = await anna.InviaModuloAsync(Percorso, Modulo(), PercorsoSalva);

        Assert.DoesNotContain("Segreto di B", await pagina.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("Negozio Srl", (await c.ComeOfflineAsync(c.B, s => s.LeggiAsync(c.AdminB, Ct))).Impostazioni.Intestatario == "Segreto di B" ? "Negozio Srl" : "alterato");
    }

    [Fact]
    public async Task AllaCassaIlClienteVedeBonificoEContrassegnoEOrdinaInContrassegnoConLaCommissione()
    {
        var (c, italia, _) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);

        using var cassa = await cliente.GetAsync("/account/checkout");
        using var ordina = await cliente.InviaModuloAsync("/account/checkout", Cassa(italia, "Contrassegno", "29.50"));

        var html = WebUtility.HtmlDecode(await cassa.Content.ReadAsStringAsync(Ct));
        Assert.Contains("name=\"Modulo.Modalita\" value=\"Bonifico\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Modulo.Modalita\" value=\"Contrassegno\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Modulo.Modalita\" value=\"Online\"", html, StringComparison.Ordinal);
        Assert.Contains($"name=\"Modulo.Atteso[{italia}:Contrassegno]\" value=\"29.50\"", html, StringComparison.Ordinal);
        Assert.Contains("commissione 4,50 EUR", string.Join(' ', html.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(ordina), WebUtility.HtmlDecode(await ordina.Content.ReadAsStringAsync(Ct)));
        var destinazione = SessioneProva.Destinazione(ordina)!;
        var id = Guid.Parse(destinazione[(destinazione.LastIndexOf('/') + 1)..]);
        var ordine = await c.OrdineAsync(id);
        Assert.Equal((ModalitaPagamento.Contrassegno, StatoOrdine.DaSpedireContrassegno, 29.50m, 4.50m), (ordine.ModalitaPagamento, ordine.Stato, ordine.TotaleLordo, ordine.CommissioneLorda));

        using var dettaglio = await cliente.GetAsync(destinazione);
        var paginaOrdine = WebUtility.HtmlDecode(await dettaglio.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Commissione contrassegno", paginaOrdine, StringComparison.Ordinal);
        Assert.Contains("Pagherai", paginaOrdine, StringComparison.Ordinal);
        Assert.Contains("Tieni pronto il contante.", paginaOrdine, StringComparison.Ordinal);
        Assert.DoesNotContain("Paga con", paginaOrdine, StringComparison.Ordinal);
        Assert.Contains("Annulla l'ordine", paginaOrdine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConIlBonificoLaPaginaDellOrdineMostraLeCoordinateLaCausaleELaScadenzaESenzaPulsantiDiPagamento()
    {
        var (c, italia, _) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);

        using var ordina = await cliente.InviaModuloAsync("/account/checkout", Cassa(italia, "Bonifico", "25.00"));
        var destinazione = SessioneProva.Destinazione(ordina)!;
        using var dettaglio = await cliente.GetAsync(destinazione);

        var html = WebUtility.HtmlDecode(await dettaglio.Content.ReadAsStringAsync(Ct));
        foreach (var atteso in new[] { "Come pagare con bonifico", "Negozio Srl", "IT60 X054 2811 1010 0000 0123 456", "BCITITMM", "Banca di Prova", "Ordine 1", "25,00 EUR", "Effettua il bonifico entro il", "Indica l'ordine nella causale." })
        {
            Assert.Contains(atteso, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Paga con", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Il pagamento online non è disponibile", html, StringComparison.Ordinal);
        Assert.DoesNotContain("La merce è riservata per te fino alle", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllaCassaUnPagamentoNonAttivoOUnTotaleSenzaLaCommissioneNonCreanoOrdini()
    {
        var (c, italia, ritiro) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);

        using var senzaCommissione = await cliente.InviaModuloAsync("/account/checkout", Cassa(italia, "Contrassegno", "25.00"));
        using var ritiroInContrassegno = await cliente.InviaModuloAsync("/account/checkout", Cassa(ritiro, "Contrassegno", "20.00"));

        Assert.Equal(HttpStatusCode.OK, senzaCommissione.StatusCode);
        Assert.Contains("cambiati", WebUtility.HtmlDecode(await senzaCommissione.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, ritiroInContrassegno.StatusCode);
        Assert.Contains("non è disponibile per questo ordine", WebUtility.HtmlDecode(await ritiroInContrassegno.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaOrdiniAsync());
    }

    [Fact]
    public async Task SenzaPagamentiManualiLaCassaOffreSoloIlPagamentoOnlineComePrima()
    {
        var (c, italia, _) = await PreparaAsync(configura: false);
        await using var _ = c;
        var cliente = await ClienteAsync(c);

        using var cassa = await cliente.GetAsync("/account/checkout");
        using var ordina = await cliente.InviaModuloAsync("/account/checkout", Cassa(italia, "Online", "25.00"));

        var html = WebUtility.HtmlDecode(await cassa.Content.ReadAsStringAsync(Ct));
        Assert.Contains("name=\"Modulo.Modalita\" value=\"Online\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"Bonifico\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"Contrassegno\"", html, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(ordina));
        var id = IdDa(ordina);
        Assert.Equal(ModalitaPagamento.Online, (await c.OrdineAsync(id)).ModalitaPagamento);
    }

    [Fact]
    public async Task InAmministrazioneIlBonificoSiSegnaPagatoESiPoiEvade()
    {
        var (c, italia, _) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var anna = await AdminAsync(c);
        using var ordina = await cliente.InviaModuloAsync("/account/checkout", Cassa(italia, "Bonifico", "25.00"));
        var id = IdDa(ordina);
        var pagina = $"/admin/negozio/ordini/{id}";

        using var prima = await anna.GetAsync(pagina);
        using var pagato = await anna.InviaModuloAsync(pagina, [], $"{pagina}?handler=Pagato");
        using var dopo = await anna.GetAsync(pagina);

        var htmlPrima = WebUtility.HtmlDecode(await prima.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Bonifico ricevuto: segna come pagato", htmlPrima, StringComparison.Ordinal);
        Assert.Contains("Annulla l'ordine", htmlPrima, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(pagato));
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(id));
        var htmlDopo = WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Pagamento registrato", htmlDopo, StringComparison.Ordinal);
        Assert.Contains("Segna come spedito", htmlDopo, StringComparison.Ordinal);
        Assert.Contains("Pagamento fuori dal sistema", htmlDopo, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"importo\"", htmlDopo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InAmministrazioneIlContrassegnoSiSpedisceEUnaConsegnaFallitaLAnnulla()
    {
        var (c, italia, _) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var anna = await AdminAsync(c);
        using var ordina = await cliente.InviaModuloAsync("/account/checkout", Cassa(italia, "Contrassegno", "29.50"));
        var id = IdDa(ordina);
        var pagina = $"/admin/negozio/ordini/{id}";

        using var prima = await anna.GetAsync(pagina);
        using var spedito = await anna.InviaModuloAsync(pagina, [], $"{pagina}?handler=Evaso");
        using var dopoSpedizione = await anna.GetAsync(pagina);
        using var fallita = await anna.InviaModuloAsync(pagina, [], $"{pagina}?handler=ConsegnaNonRiuscita");

        var htmlPrima = WebUtility.HtmlDecode(await prima.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Segna come spedito", htmlPrima, StringComparison.Ordinal);
        Assert.Contains("Commissione contrassegno", htmlPrima, StringComparison.Ordinal);
        Assert.DoesNotContain("Bonifico ricevuto", htmlPrima, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(spedito));
        Assert.Contains("Consegna non riuscita", WebUtility.HtmlDecode(await dopoSpedizione.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(fallita));
        Assert.Equal(StatoOrdine.Annullato, await c.StatoOrdineAsync(id));
    }

    [Fact]
    public async Task LeAzioniSugliOrdiniManualiRichiedonoLAmministratoreDelTenantGiusto()
    {
        var (c, italia, _) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        using var ordina = await cliente.InviaModuloAsync("/account/checkout", Cassa(italia, "Bonifico", "25.00"));
        var id = IdDa(ordina);
        var pagina = $"/admin/negozio/ordini/{id}";
        var bruno = c.Web.SessioneSu(c.B.Host);
        await bruno.AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);

        using var comeCliente = await cliente.InviaModuloAsync(pagina, [], $"{pagina}?handler=Pagato");
        using var comeBruno = await bruno.InviaModuloAsync($"/admin/negozio/ordini/{id}", [], $"/admin/negozio/ordini/{id}?handler=Pagato");

        Assert.NotEqual(HttpStatusCode.OK, comeCliente.StatusCode);
        Assert.True(comeBruno.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Redirect or HttpStatusCode.BadRequest, comeBruno.StatusCode.ToString());
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(id));
    }
}

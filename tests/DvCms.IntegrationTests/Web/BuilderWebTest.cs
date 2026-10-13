using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DvCms.Contracts.Cms;
using DvCms.Contracts.Media;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Piattaforma;
using DvCms.Contracts.Temi;
using DvCms.Core.Builder;
using DvCms.Core.Tenancy;
using DvCms.DTO.Builder;
using DvCms.DTO.Media;
using DvCms.DTO.Negozio;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.Infrastructure.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Cms;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Page builder (M4, E-001…E-005): creazione, salvataggio, policy del template, concorrenza e rendering pubblico.</summary>
public partial class BuilderWebTest
{
    private static readonly RegistroComponenti _registro = new(RegistroComponenti.Predefiniti);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DocumentoCms Iniziale(string template = RegistroTemplateBuilder.IntestazioneFissa) =>
        _registro.Normalizza(new RegistroTemplateBuilder(RegistroTemplateBuilder.Predefiniti).Trova(template, 1)!.DocumentoIniziale());

    private static NodoCms Nodo(string id, string tipo, Dictionary<string, string>? proprieta = null, Dictionary<string, string>? stili = null, params NodoCms[] figli) =>
        new(id, tipo, proprieta ?? [], stili ?? [], figli);

    private static DocumentoCms ConCorpo(DocumentoCms documento, params NodoCms[] figli) =>
        documento with { Nodi = [.. documento.Nodi.Select(n => n.Id == "corpo" ? n with { Figli = figli } : n)] };

    private static string Progetto(DocumentoCms documento) => AdapterGrapesJs.AProgetto(_registro.Normalizza(documento));

    private static string Corpo(string progetto, string token, bool automatico = false) => JsonSerializer.Serialize(new { token, progetto, automatico });

    [Fact]
    public async Task LaPaginaDelBuilderEIsolataESalvaConLeRegoleDelServer()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "benvenuto");

        // La pagina del builder: asset isolati dalla stessa origine, nessuno script in linea, mai in cache.
        using var pagina = await c.Sessione.GetAsync($"/admin/pagine/{id}/builder");
        var html = await pagina.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains("/lib/page-builder/page-builder.js", html, StringComparison.Ordinal);
        Assert.Contains($"data-url-salva=\"/admin/pagine/{id}/builder?handler=Salva\"", html, StringComparison.Ordinal);

        // Il canvas usa il tema del sito (qui il predefinito): foglio del tema e skin dei componenti, classe e variabili del corpo.
        Assert.Contains("data-tema=", html, StringComparison.Ordinal);
        Assert.Contains("/css/temi/classico-v1.css", html, StringComparison.Ordinal);
        Assert.Contains("/css/temi/classico-componenti.css", html, StringComparison.Ordinal);
        Assert.Contains("tema-classico", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", html);
        Assert.Contains("script-src 'self'", pagina.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Contains("no-store", pagina.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);

        // Lo stato: progetto del template, catalogo e vincoli (nodi bloccati e slot).
        var stato = await c.StatoAsync(id);
        Assert.Equal("pagina-con-intestazione-fissa", stato.GetProperty("template").GetString());
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(stato.GetProperty("progetto").GetRawText()),
            System.Text.Json.Nodes.JsonNode.Parse(Progetto(Iniziale()))));
        Assert.Equal(19, stato.GetProperty("catalogo").GetArrayLength());
        Assert.Equal(7, stato.GetProperty("versioneTemplate").GetInt32());
        Assert.True(stato.GetProperty("vincoli").GetProperty("nodi").GetProperty("avviso").GetProperty("bloccato").GetBoolean());
        Assert.Equal(30, stato.GetProperty("vincoli").GetProperty("nodi").GetProperty("corpo").GetProperty("slot").GetProperty("figliMassimi").GetInt32());

        // Un salvataggio valido: nuova revisione con documento e progetto rigenerato dal server.
        var documento = ConCorpo(Iniziale(), Nodo("c1", "contenuto.titolo", new() { ["testo"] = "Sezione uno" }), Nodo("c2", "contenuto.testo", new() { ["testo"] = "Un paragrafo.\n\nUn altro." }));
        using var salvato = await c.SalvaAsync(id, stato.GetProperty("token").GetString()!, Progetto(documento));
        var risposta = await Json(salvato);
        Assert.Equal(HttpStatusCode.OK, salvato.StatusCode);
        Assert.True(risposta.GetProperty("ok").GetBoolean());
        Assert.True(risposta.GetProperty("nuovaRevisione").GetBoolean());
        Assert.Equal(2, risposta.GetProperty("numeroRevisione").GetInt32());
        var revisioni = await c.RevisioniAsync(id);
        Assert.Equal(2, revisioni.Count);
        Assert.Equal(SerializzatoreDocumento.Scrivi(_registro.Normalizza(documento)), revisioni[^1].DocumentoRenderJson);
        Assert.Equal(Progetto(documento), revisioni[^1].ProgettoEditorJson);
        Assert.Equal(SerializzatoreDocumento.Scrivi(Iniziale()), revisioni[0].DocumentoRenderJson);

        // Lo stesso contenuto con il token nuovo non crea una revisione; la pagina di modifica non mostra il testo libero.
        using var uguale = await c.SalvaAsync(id, risposta.GetProperty("token").GetString()!, Progetto(documento));
        var rispostaUguale = await Json(uguale);
        Assert.True(rispostaUguale.GetProperty("ok").GetBoolean());
        Assert.False(rispostaUguale.GetProperty("nuovaRevisione").GetBoolean());
        Assert.Equal(2, (await c.RevisioniAsync(id)).Count);
        using var modifica = await c.Sessione.GetAsync(percorso);
        var htmlModifica = await modifica.Content.ReadAsStringAsync(Ct);
        Assert.Contains($"/admin/pagine/{id}/builder", htmlModifica, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Contenuto\"", htmlModifica, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnSalvataggioConUnTokenVecchioEUnConflittoENonSovrascriveNulla()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, _) = await c.CreaPaginaAsync("pagina-libera", "libera");
        var tokenIniziale = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        var primo = new DocumentoCms(1, [Nodo("s1", "layout.sezione")]);
        var secondo = new DocumentoCms(1, [Nodo("s2", "layout.sezione")]);

        using var riuscito = await c.SalvaAsync(id, tokenIniziale, Progetto(primo));
        using var obsoleto = await c.SalvaAsync(id, tokenIniziale, Progetto(secondo));

        Assert.Equal(HttpStatusCode.OK, riuscito.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, obsoleto.StatusCode);
        var revisioni = await c.RevisioniAsync(id);
        Assert.Equal(2, revisioni.Count);
        Assert.Equal(SerializzatoreDocumento.Scrivi(primo), revisioni[^1].DocumentoRenderJson);
    }

    public static TheoryData<string, string> PayloadManomessi() => new()
    {
        { "NodoBloccatoMancante", "senza-avviso" },
        { "NodoBloccatoModificato", "avviso-cambiato" },
        { "NodoBloccatoModificato", "intestazione-livello" },
        { "NodoBloccatoSpostato", "ordine" },
        { "NodoFuoriSlot", "extra-radice" },
        { "TipoNonAmmessoNelloSlot", "sezione-nello-slot" },
        { "ProprietaNonValida", "javascript-url" },
        { "TipoNonSupportato", "script" },
        { "ClassiNonSupportate", "classi" },
        { "AttributoNonSupportato", "onclick" },
        { "NodoDuplicato", "id-duplicati" },
        { "RisorsaNonDisponibile", "risorsa-inesistente" },
    };

    private static string Manomesso(string caso)
    {
        var iniziale = Iniziale();
        NodoCms Cambia(NodoCms nodo, string id, Func<NodoCms, NodoCms> f) => nodo.Id == id ? f(nodo) : nodo with { Figli = [.. nodo.Figli.Select(n => Cambia(n, id, f))] };
        DocumentoCms Modifica(string id, Func<NodoCms, NodoCms> f) => iniziale with { Nodi = [.. iniziale.Nodi.Select(n => Cambia(n, id, f))] };

        switch (caso)
        {
            case "senza-avviso":
                return Progetto(iniziale with { Nodi = [.. iniziale.Nodi.Where(n => n.Id != "avviso")] });
            case "avviso-cambiato":
                return Progetto(Modifica("avviso-testo", n => n with { Proprieta = new Dictionary<string, string> { ["testo"] = "Nessun avviso" } }));
            case "intestazione-livello":
                return Progetto(Modifica("intestazione-titolo", n => n with { Proprieta = new Dictionary<string, string> { ["testo"] = "x", ["livello"] = "3" } }));
            case "ordine":
                return Progetto(iniziale with { Nodi = [iniziale.Nodi[1], iniziale.Nodi[0], iniziale.Nodi[2]] });
            case "extra-radice":
                return Progetto(iniziale with { Nodi = [.. iniziale.Nodi, Nodo("extra", "layout.sezione")] });
            case "sezione-nello-slot":
                return Progetto(ConCorpo(iniziale, Nodo("annidata", "layout.sezione")));
            case "javascript-url":
                return Progetto(ConCorpo(iniziale, Nodo("b", "contenuto.pulsante", new() { ["etichetta"] = "Clic", ["url"] = "javascript:alert(document.cookie)" })));
            case "script":
                return Progetto(iniziale).Replace("\"type\":\"contenuto.testo\"", "\"type\":\"script\"", StringComparison.Ordinal);
            case "classi":
                return Progetto(iniziale).Replace("\"type\":\"layout.sezione\"", "\"type\":\"layout.sezione\",\"classes\":[\"rosso\"]", StringComparison.Ordinal);
            case "onclick":
                return Progetto(iniziale).Replace("\"attributes\":{\"id\":\"avviso\"", "\"attributes\":{\"onclick\":\"alert(1)\",\"id\":\"avviso\"", StringComparison.Ordinal);
            case "id-duplicati":
                return Progetto(ConCorpo(iniziale, Nodo("avviso-testo", "contenuto.testo", new() { ["testo"] = "doppio" })));
            case "risorsa-inesistente":
                return Progetto(ConCorpo(iniziale, Nodo("img", "contenuto.immagine", new() { ["idRisorsa"] = Guid.NewGuid().ToString("D"), ["alt"] = "x" })));
            default:
                throw new ArgumentOutOfRangeException(nameof(caso));
        }
    }

    [Theory]
    [MemberData(nameof(PayloadManomessi))]
    public async Task UnPayloadManomessoViaApiEDifesoDalServer(string codiceAtteso, string caso)
    {
        await using var c = await Contesto.CreaAsync();
        var (id, _) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "protetta");
        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;

        using var risposta = await c.SalvaAsync(id, token, Manomesso(caso));
        var json = await Json(risposta);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Contains(json.GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == codiceAtteso);
        Assert.Single(await c.RevisioniAsync(id));
    }

    [Fact]
    public async Task LeRisorseDiUnAltroTenantOPrivateNonSiPossonoUsare()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, _) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "immagini");
        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        var propria = await c.CaricaAsync(c.Web.A);
        var altrui = await c.CaricaAsync(c.Web.B);
        var privata = await c.CaricaAsync(c.Web.A, visibilita: DvCms.Models.Media.VisibilitaMedia.Privata);

        foreach (var risorsa in new[] { altrui, privata })
        {
            using var rifiutata = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("img", "contenuto.immagine", new() { ["idRisorsa"] = risorsa.ToString("D"), ["alt"] = "x" }))));
            Assert.Equal(HttpStatusCode.BadRequest, rifiutata.StatusCode);
            Assert.Contains((await Json(rifiutata)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "RisorsaNonDisponibile");
        }

        using var accettata = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("img", "contenuto.immagine", new() { ["idRisorsa"] = propria.ToString("D"), ["alt"] = "Foto" }))));
        Assert.Equal(HttpStatusCode.OK, accettata.StatusCode);
    }

    [Fact]
    public async Task LaPaginaPubblicataSiDisegnaDalDocumentoConDueTemiEIlTestoEAlwaysCodificato()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "vetrina", slug: "vetrina");
        var immagine = await c.CaricaAsync(c.Web.A);
        var stato = await c.StatoAsync(id);
        var documento = ConCorpo(
            Iniziale() with { Nodi = [.. Iniziale().Nodi.Select(n => n.Id == "intestazione" ? n with { Figli = [n.Figli[0] with { Proprieta = new Dictionary<string, string> { ["testo"] = "Titolo <b>grosso</b> & \"bello\"", ["livello"] = "1" } }] } : n)] },
            Nodo("t", "contenuto.titolo", new() { ["testo"] = "Sezione", ["livello"] = "3" }),
            Nodo("p", "contenuto.testo", new() { ["testo"] = "<script>alert(1)</script>\nseconda riga\n\nNuovo paragrafo" }),
            Nodo("i", "contenuto.immagine", new() { ["idRisorsa"] = immagine.ToString("D"), ["alt"] = "Una foto \"nitida\"" }),
            Nodo("col", "layout.colonne", null, null,
                Nodo("c1", "layout.colonna", null, null, Nodo("b1", "contenuto.pulsante", new() { ["etichetta"] = "Scopri", ["url"] = "/prodotti" })),
                Nodo("c2", "layout.colonna", null, null, Nodo("b2", "contenuto.pulsante", new() { ["etichetta"] = "Esterno", ["url"] = "https://esempio.it/x?a=1&b=2" }, new() { ["variante"] = "secondario" }))));
        using var salvato = await c.SalvaAsync(id, stato.GetProperty("token").GetString()!, Progetto(documento));
        Assert.Equal(HttpStatusCode.OK, salvato.StatusCode);

        // Una bozza non è pubblica; l'anteprima la mostra.
        using var nonPubblica = await c.Web.SessioneA().GetAsync("/vetrina");
        Assert.Equal(HttpStatusCode.NotFound, nonPubblica.StatusCode);
        using var anteprima = await c.Sessione.GetAsync($"/admin/pagine/{id}/anteprima");
        Assert.Contains("dv-documento", await anteprima.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        await c.PubblicaAsync(percorso);
        using var pubblica = await c.Web.SessioneA().GetAsync("/vetrina");
        var html = await pubblica.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, pubblica.StatusCode);
        Assert.Contains("tema-classico", html, StringComparison.Ordinal);
        Assert.Contains("<h1 class=\"dv-titolo dv-allineamento-centro\">Titolo &lt;b&gt;grosso&lt;/b&gt; &amp; &quot;bello&quot;</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h3 class=\"dv-titolo dv-allineamento-sinistra\">Sezione</h3>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; <br /> seconda riga", Regex.Replace(html, "\\s+", " "), StringComparison.Ordinal);
        Assert.Contains("Nuovo paragrafo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains($"<img src=\"/media/{immagine:D}\" alt=\"Una foto &quot;nitida&quot;\" loading=\"lazy\" />", html, StringComparison.Ordinal);
        Assert.Contains("class=\"pulsante dv-pulsante dv-pulsante-primario\" href=\"/prodotti\">Scopri</a>", html, StringComparison.Ordinal);
        Assert.Contains("dv-pulsante-secondario\" href=\"https://esempio.it/x?a=1&amp;b=2\">Esterno</a>", html, StringComparison.Ordinal);
        Assert.Contains("dv-colonne dv-colonne-2", html, StringComparison.Ordinal);
        Assert.Contains("dv-sfondo-scuro", html, StringComparison.Ordinal);
        Assert.Contains("/css/componenti.css", html, StringComparison.Ordinal);
        Assert.Contains("/css/temi/classico-componenti.css", html, StringComparison.Ordinal);
        using var immagineServita = await c.Web.SessioneA().GetAsync($"/media/{immagine:D}");
        Assert.Equal(HttpStatusCode.OK, immagineServita.StatusCode);

        // Stesso documento, altro tema: cambia il contenitore del tema, non il contenuto.
        await c.ImpostaTemaAsync("moderno");
        using var moderno = await c.Web.SessioneA().GetAsync("/vetrina");
        var htmlModerno = await moderno.Content.ReadAsStringAsync(Ct);
        Assert.Contains("tema-moderno", htmlModerno, StringComparison.Ordinal);
        Assert.DoesNotContain("tema-classico", htmlModerno, StringComparison.Ordinal);
        Assert.Contains("/css/temi/moderno-componenti.css", htmlModerno, StringComparison.Ordinal);
        Assert.DoesNotContain("classico-componenti", htmlModerno, StringComparison.Ordinal);
        Assert.Equal(Estrai(html, "<article class=\"dv-documento\">", "</article>"), Estrai(htmlModerno, "<article class=\"dv-documento\">", "</article>"));
    }

    [Fact]
    public async Task IComponentiAvanzatiEdIlAnimazioniSiDisegnanoConSoloClassiDaElenco()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "avanzata", slug: "avanzata");
        var stato = await c.StatoAsync(id);
        var documento = ConCorpo(
            Iniziale(),
            Nodo("h", "contenuto.hero", new() { ["titolo"] = "Benvenuti <b>qui</b>", ["sottotitolo"] = "Sotto", ["etichettaPulsante"] = "Vai", ["urlPulsante"] = "/prodotti" }, new() { ["altezza"] = "alta", ["animazione"] = "sale", ["ritardoAnimazione"] = "300" }),
            Nodo("c", "contenuto.cta", new() { ["titolo"] = "Scrivici", ["testo"] = "Rispondiamo presto.", ["etichetta"] = "Contatti", ["url"] = "https://esempio.it/contatti" }, new() { ["sfondo"] = "primario" }),
            Nodo("f", "contenuto.fisarmonica", null, new() { ["animazione"] = "dissolvenza" },
                Nodo("v1", "contenuto.voce", new() { ["domanda"] = "Come funziona?", ["risposta"] = "Primo.\n\n<script>alert(1)</script>" }),
                Nodo("v2", "contenuto.voce", new() { ["domanda"] = "Quanto costa?", ["risposta"] = "Poco." })));
        using var salvato = await c.SalvaAsync(id, stato.GetProperty("token").GetString()!, Progetto(documento));
        Assert.Equal(HttpStatusCode.OK, salvato.StatusCode);
        await c.PubblicaAsync(percorso);

        using var pubblica = await c.Web.SessioneA().GetAsync("/avanzata");
        var html = await pubblica.Content.ReadAsStringAsync(Ct);

        Assert.Contains("<section class=\"dv-hero dv-altezza-alta dv-sfondo-scuro dv-allineamento-sinistra dv-anim dv-anim-sale dv-ritardo-300\">", html, StringComparison.Ordinal);
        Assert.Contains("<h1 class=\"dv-hero-titolo\">Benvenuti &lt;b&gt;qui&lt;/b&gt;</h1>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/prodotti\">Vai</a>", html, StringComparison.Ordinal);
        Assert.Contains("dv-cta dv-sfondo-primario", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"dv-fisarmonica dv-anim dv-anim-dissolvenza dv-ritardo-0\">", html, StringComparison.Ordinal);

        Assert.Contains("<summary>Come funziona?</summary>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        // Lo script delle animazioni è l'unico, è del sito e sta in testa; le pagine senza builder non lo caricano.
        Assert.Single(Regex.Matches(html, "<script"));
        Assert.Contains("src=\"/js/animazioni.js", html, StringComparison.Ordinal);
        using var script = await c.Web.SessioneA().GetAsync("/js/animazioni.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }

    [Fact]
    public async Task CaratteristicheETestimonianzeSiDisegnanoConTestoCodificatoESoloClassiDaElenco()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "vetrina-due", slug: "vetrina-due");
        var stato = await c.StatoAsync(id);
        var documento = ConCorpo(
            Iniziale(),
            Nodo("g", "contenuto.caratteristiche", null, new() { ["colonne"] = "2" },
                Nodo("c1", "contenuto.caratteristica", new() { ["titolo"] = "Veloce <i>sempre</i>", ["descrizione"] = "Parte subito." }),
                Nodo("c2", "contenuto.caratteristica", new() { ["titolo"] = "Sicuro" })),
            Nodo("t", "contenuto.testimonianze", null, new() { ["animazione"] = "sale" },
                Nodo("q", "contenuto.testimonianza", new() { ["citazione"] = "Ottimo.\n\n<script>alert(1)</script>", ["autore"] = "Ada", ["ruolo"] = "CTO" }),
                Nodo("q2", "contenuto.testimonianza", new() { ["citazione"] = "Bene", ["autore"] = "Bruno" })));
        using var salvato = await c.SalvaAsync(id, stato.GetProperty("token").GetString()!, Progetto(documento));
        Assert.Equal(HttpStatusCode.OK, salvato.StatusCode);
        await c.PubblicaAsync(percorso);

        using var pubblica = await c.Web.SessioneA().GetAsync("/vetrina-due");
        var html = await pubblica.Content.ReadAsStringAsync(Ct);

        Assert.Contains("<div class=\"dv-caratteristiche dv-colonne dv-colonne-2\">", html, StringComparison.Ordinal);
        Assert.Contains("<h3 class=\"dv-caratteristica-titolo\">Veloce &lt;i&gt;sempre&lt;/i&gt;</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Parte subito.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"dv-testimonianze dv-anim dv-anim-sale dv-ritardo-0\">", html, StringComparison.Ordinal);
        Assert.Contains("<figure class=\"dv-testimonianza\">", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Ada</strong>, CTO", Regex.Replace(html, "\\s+", " "), StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>sempre", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaGrigliaDiProdottiMostraIPubblicatiDelTenantConNomeCodificatoEScompareSeIlNegozioSiSpegne()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "negozio-home", slug: "negozio-home");
        var stato = await c.StatoAsync(id);
        var documento = ConCorpo(Iniziale(), Nodo("p", "negozio.griglia-prodotti", new() { ["quanti"] = "3" }, new() { ["colonne"] = "2" }));
        using var salvato = await c.SalvaAsync(id, stato.GetProperty("token").GetString()!, Progetto(documento));
        Assert.Equal(HttpStatusCode.OK, salvato.StatusCode);
        await c.PubblicaAsync(percorso);
        var piattaforma = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
        await c.ImpostaNegozioAsync(piattaforma, true);
        await c.CreaProdottoAsync(c.Web.A, "Libro <b>nuovo</b>", 10m);
        await c.CreaProdottoAsync(c.Web.A, "Bozza segreta", 10m, StatoProdotto.Bozza);

        await c.ImpostaNegozioAsync(piattaforma, false);
        using var spento = await c.Web.SessioneA().GetAsync("/negozio-home");
        await c.ImpostaNegozioAsync(piattaforma, true);
        using var acceso = await c.Web.SessioneA().GetAsync("/negozio-home");
        var html = await acceso.Content.ReadAsStringAsync(Ct);
        await c.ImpostaNegozioAsync(piattaforma, false);
        using var dopo = await c.Web.SessioneA().GetAsync("/negozio-home");

        Assert.DoesNotContain("dv-griglia-prodotti", await spento.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("<ul class=\"dv-griglia-prodotti dv-griglia-prodotti-colonne-2\">", html, StringComparison.Ordinal);
        Assert.Contains("Libro &lt;b&gt;nuovo&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("10,00 EUR", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>nuovo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Bozza segreta", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dv-griglia-prodotti", await dopo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaSchedaProdottoVuoleUnProdottoDelSitoEMostraSoloQuelloPubblicato()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "scheda", slug: "scheda");
        var piattaforma = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
        await c.ImpostaNegozioAsync(piattaforma, true);
        await c.ImpostaNegozioAsync(piattaforma, true, c.Web.B);
        var pubblicato = await c.CreaProdottoAsync(c.Web.A, "Quaderno <i>a righe</i>", 4.50m);
        var bozza = await c.CreaProdottoAsync(c.Web.A, "Prodotto in bozza", 5m, StatoProdotto.Bozza);
        var altrui = await c.CreaProdottoAsync(c.Web.B, "Prodotto di un altro tenant", 5m);
        var stato = await c.StatoAsync(id);
        var token = stato.GetProperty("token").GetString()!;

        using var inesistente = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = Guid.NewGuid().ToString("D") }))));
        using var diAltri = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = altrui.ToString("D") }))));
        using var buono = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = pubblicato.ToString("D") }))));
        await c.PubblicaAsync(percorso);
        using var vista = await c.Web.SessioneA().GetAsync("/scheda");
        var html = await vista.Content.ReadAsStringAsync(Ct);
        var nuovoToken = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        using var conBozza = await c.SalvaAsync(id, nuovoToken, Progetto(ConCorpo(Iniziale(), Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = bozza.ToString("D") }))));
        await c.PubblicaAsync(percorso);
        using var vistaBozza = await c.Web.SessioneA().GetAsync("/scheda");

        Assert.Contains((await Json(inesistente)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "ProdottoNonDisponibile");
        Assert.Contains((await Json(diAltri)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "ProdottoNonDisponibile");
        Assert.Equal(HttpStatusCode.OK, buono.StatusCode);
        Assert.Contains("Quaderno &lt;i&gt;a righe&lt;/i&gt;", html, StringComparison.Ordinal);
        Assert.Contains("4,50 EUR", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/negozio/{pubblicato:D}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>a righe", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, conBozza.StatusCode);
        Assert.DoesNotContain("Prodotto in bozza", await vistaBozza.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.DoesNotContain("dv-scheda-prodotto", await vistaBozza.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaGrigliaDiCategoriaVuoleUnaCategoriaDelSitoEMostraSoloIProdottiVisibiliDiQuellaCategoria()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "cat", slug: "cat");
        var piattaforma = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
        await c.ImpostaNegozioAsync(piattaforma, true);
        await c.ImpostaNegozioAsync(piattaforma, true, c.Web.B);
        var libri = await c.CreaCategoriaAsync(c.Web.A, "Libri");
        var altrui = await c.CreaCategoriaAsync(c.Web.B, "Di B");
        await c.CreaProdottoAsync(c.Web.A, "Romanzo", 12m, categoria: libri);
        await c.CreaProdottoAsync(c.Web.A, "Penna", 2m);
        await c.CreaProdottoAsync(c.Web.A, "Libro in bozza", 9m, StatoProdotto.Bozza, libri);
        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;

        using var inesistente = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("g", "negozio.griglia-categoria", new() { ["idCategoria"] = Guid.NewGuid().ToString("D") }))));
        using var diAltri = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("g", "negozio.griglia-categoria", new() { ["idCategoria"] = altrui.ToString("D") }))));
        using var buona = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("g", "negozio.griglia-categoria", new() { ["idCategoria"] = libri.ToString("D"), ["quanti"] = "3" }))));
        await c.PubblicaAsync(percorso);
        var html = await (await c.Web.SessioneA().GetAsync("/cat")).Content.ReadAsStringAsync(Ct);

        Assert.Contains((await Json(inesistente)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "CategoriaNonDisponibile");
        Assert.Contains((await Json(diAltri)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "CategoriaNonDisponibile");
        Assert.Equal(HttpStatusCode.OK, buona.StatusCode);
        Assert.Contains("Romanzo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Penna", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Libro in bozza", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaPaginaDellaVersione1PassaAllUltimaVersioneSoloSeIlDocumentoEValido()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "vecchia");
        await c.ImpostaVersioneModelloAsync(id, 1);
        var documentoV1 = ConCorpo(Iniziale(), Nodo("t", "contenuto.testo", new() { ["testo"] = "Scritto con la versione 1" }));
        var stato = await c.StatoAsync(id);
        Assert.Equal(1, stato.GetProperty("versioneTemplate").GetInt32());
        using var salvato = await c.SalvaAsync(id, stato.GetProperty("token").GetString()!, Progetto(documentoV1));
        Assert.Equal(HttpStatusCode.OK, salvato.StatusCode);

        // Con la versione 1 il corpo non accetta un hero.
        var token = (await Json(salvato)).GetProperty("token").GetString()!;
        using var rifiutato = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("h", "contenuto.hero", new() { ["titolo"] = "x" }))));
        Assert.Contains((await Json(rifiutato)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "TipoNonAmmessoNelloSlot");

        using var modifica = await c.Sessione.GetAsync(percorso);
        Assert.Contains("Passa all&#x27;ultima versione del modello", await modifica.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var tokenModulo = await c.TokenModuloAsync(percorso);
        using var passaggio = await c.Sessione.InviaModuloAsync(percorso, [new("TokenConcorrenza", tokenModulo)], $"{percorso}?handler=AggiornaModello");
        Assert.True(SessioneProva.EReindirizzamento(passaggio));

        var revisioni = await c.RevisioniAsync(id);
        Assert.Equal(7, revisioni[^1].VersioneModello);
        Assert.Equal(revisioni[^2].DocumentoRenderJson, revisioni[^1].DocumentoRenderJson);
        var dopo = await c.StatoAsync(id);
        Assert.Equal(7, dopo.GetProperty("versioneTemplate").GetInt32());
        using var oraAccettato = await c.SalvaAsync(id, dopo.GetProperty("token").GetString()!, Progetto(ConCorpo(Iniziale(), Nodo("h", "contenuto.hero", new() { ["titolo"] = "x" }))));
        Assert.Equal(HttpStatusCode.OK, oraAccettato.StatusCode);
    }

    [Fact]
    public async Task IlPassaggioDiVersioneEIlRifiutatoSeIlDocumentoNonEConformeAlNuovoTemplate()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "danneggiata");
        await c.ImpostaVersioneModelloAsync(id, 1);
        await c.ScriviDocumentoDirettoAsync(id, SerializzatoreDocumento.Scrivi(Iniziale() with { Nodi = [.. Iniziale().Nodi.Where(n => n.Id != "avviso")] }));
        var tokenModulo = await c.TokenModuloAsync(percorso);

        using var passaggio = await c.Sessione.InviaModuloAsync(percorso, [new("TokenConcorrenza", tokenModulo)], $"{percorso}?handler=AggiornaModello");
        using var pagina = await c.Sessione.GetAsync(percorso);

        Assert.True(SessioneProva.EReindirizzamento(passaggio));
        Assert.Single(await c.RevisioniAsync(id));
        Assert.Equal(1, (await c.RevisioniAsync(id))[0].VersioneModello);
    }

    [Fact]
    public async Task IlTestoFormattatoSiDisegnaConElementiFissiEUnMarkupNonValidoEDifeso()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "formattata", slug: "formattata");
        var stato = await c.StatoAsync(id);
        var markup = "**Grassetto** <script>alert(1)</script> *corsivo* [Interno](/chi-siamo) [Esterno](https://esempio.it/x?a=1&b=2)\nSeconda riga\n\n- uno\n- **due**";
        using var salvato = await c.SalvaAsync(id, stato.GetProperty("token").GetString()!, Progetto(ConCorpo(Iniziale(), Nodo("r", "contenuto.testo-ricco", new() { ["testo"] = markup }))));
        Assert.Equal(HttpStatusCode.OK, salvato.StatusCode);
        await c.PubblicaAsync(percorso);

        using var pubblica = await c.Web.SessioneA().GetAsync("/formattata");
        var html = Regex.Replace(await pubblica.Content.ReadAsStringAsync(Ct), "\\s+", " ");

        Assert.Contains("<strong>Grassetto</strong> &lt;script&gt;alert(1)&lt;/script&gt; <em>corsivo</em>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"/chi-siamo\" rel=\"noopener\">Interno</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://esempio.it/x?a=1&amp;b=2\" rel=\"noopener\">Esterno</a>", html, StringComparison.Ordinal);
        Assert.Contains("<br />Seconda riga", html, StringComparison.Ordinal);
        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("<li><strong>due</strong></li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);

        // Markup fuori dalla grammatica: rifiutato dal server, nulla di salvato.
        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        foreach (var cattivo in new[] { "**non chiuso", "[x](javascript:alert(1))", "[x](http://non-sicuro.example)", "testo ] spaiato" })
        {
            using var rifiutato = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("r", "contenuto.testo-ricco", new() { ["testo"] = cattivo }))));
            Assert.Equal(HttpStatusCode.BadRequest, rifiutato.StatusCode);
            Assert.Contains((await Json(rifiutato)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "ProprietaNonValida" && e.GetProperty("idNodo").GetString() == "r");
        }

        Assert.Equal(2, (await c.RevisioniAsync(id)).Count);

        // Un documento alterato nel database con un collegamento ostile non produce il collegamento né script.
        await c.ScriviDocumentoDirettoAsync(id, """{"versioneSchema":1,"nodi":[{"id":"r","tipo":"contenuto.testo-ricco","proprieta":{"testo":"[clic](javascript:alert(1)) **ok**"},"stili":{},"figli":[]}]}""");
        using var alterata = await c.Web.SessioneA().GetAsync("/formattata");
        var htmlAlterato = await alterata.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("javascript:", htmlAlterato, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>ok", htmlAlterato, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnDocumentoRovinatoNelDatabaseNonProduceMaHtmlDelCostruttore()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-libera", "rovinata", slug: "rovinata");
        await c.PubblicaAsync(percorso);
        await c.ScriviDocumentoDirettoAsync(id, """{"versioneSchema":1,"nodi":[{"id":"x","tipo":"script","proprieta":{"testo":"<script>alert(1)</script>"},"stili":{},"figli":[]},{"id":"y","tipo":"contenuto.pulsante","proprieta":{"etichetta":"Clic","url":"javascript:alert(1)"},"stili":{},"figli":[]},{"id":"z","tipo":"contenuto.titolo","proprieta":{"testo":"Visibile","livello":"9"},"stili":{"allineamento":"destra\" onmouseover=\"x"},"figli":[]}]}""");

        using var risposta = await c.Web.SessioneA().GetAsync("/rovinata");
        var html = await risposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.DoesNotContain("alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onmouseover", html, StringComparison.Ordinal);
        Assert.Contains("<h2 class=\"dv-titolo dv-allineamento-sinistra\">Visibile</h2>", html, StringComparison.Ordinal);

        await c.ScriviDocumentoDirettoAsync(id, "non json");
        using var illeggibile = await c.Web.SessioneA().GetAsync("/rovinata");
        Assert.Equal(HttpStatusCode.OK, illeggibile.StatusCode);
        Assert.DoesNotContain("dv-titolo", await illeggibile.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeAltreModificheDellaPaginaEIlRipristinoPortanoConSeIlDocumento()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-libera", "bozza");
        var documento = new DocumentoCms(1, [Nodo("s", "layout.sezione", null, null, Nodo("t", "contenuto.titolo", new() { ["testo"] = "Ciao" }))]);
        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        using var salvato = await c.SalvaAsync(id, token, Progetto(documento));
        var atteso = SerializzatoreDocumento.Scrivi(documento);

        // Modifica del titolo e dei campi SEO dal modulo: nuova revisione, stesso documento.
        var tokenModulo = await c.TokenModuloAsync(percorso);
        using var modifica = await c.Sessione.InviaModuloAsync(
            percorso,
            [new("Titolo", "Titolo nuovo"), new("Slug", "bozza"), new("TitoloSeo", "SEO"), new("TokenConcorrenza", tokenModulo)],
            $"{percorso}?handler=Salva");
        Assert.True(SessioneProva.EReindirizzamento(modifica));
        var revisioni = await c.RevisioniAsync(id);
        Assert.Equal("Titolo nuovo", revisioni[^1].Titolo);
        Assert.Equal(atteso, revisioni[^1].DocumentoRenderJson);
        Assert.Equal(3, revisioni.Count);

        // Ripristino della revisione 1 (documento vuoto del template): torna come nuova bozza con il suo documento.
        var tokenRipristino = await c.TokenModuloAsync(percorso);
        using var ripristino = await c.Sessione.InviaModuloAsync(
            percorso,
            [new("TokenConcorrenza", tokenRipristino)],
            $"{percorso}?handler=Ripristina&idRevisione={revisioni[0].Id}");
        Assert.True(SessioneProva.EReindirizzamento(ripristino));
        var dopo = await c.RevisioniAsync(id);
        Assert.Equal(4, dopo.Count);
        Assert.Equal(revisioni[0].DocumentoRenderJson, dopo[^1].DocumentoRenderJson);
        Assert.Equal(revisioni[0].ProgettoEditorJson, dopo[^1].ProgettoEditorJson);
    }

    [Fact]
    public async Task IlSalvataggioAutomaticoSostituisceSoloLAutomaticoInCimaDelloStessoAutoreNonPubblicato()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, percorso) = await c.CreaPaginaAsync("pagina-libera", "auto", slug: "auto");
        DocumentoCms Doc(string testo) => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("t", "contenuto.titolo", new() { ["testo"] = testo }))]);
        async Task<(JsonElement Risposta, string Token)> Salva(string testo, bool automatico, string token, SessioneProva? sessione = null)
        {
            using var risposta = await c.SalvaAsync(id, token, Progetto(Doc(testo)), automatico, sessione);
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            var json = await Json(risposta);
            return (json, json.GetProperty("token").GetString()!);
        }

        async Task<string> Numeri() => string.Join(",", (await c.RevisioniAsync(id)).Select(r => $"{r.NumeroRevisione}{(r.SalvataggioAutomatico ? "a" : "m")}"));

        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        (_, token) = await Salva("uno", automatico: false, token);
        Assert.Equal("1m,2m", await Numeri());

        // Due automatici di fila: il secondo sostituisce il primo (la cronologia non cresce).
        (_, token) = await Salva("due", automatico: true, token);
        Assert.Equal("1m,2m,3a", await Numeri());
        (_, token) = await Salva("tre", automatico: true, token);
        Assert.Equal("1m,2m,4a", await Numeri());

        // Il salvataggio manuale successivo sostituisce l'automatico in cima e resta manuale.
        (_, token) = await Salva("quattro", automatico: false, token);
        Assert.Equal("1m,2m,5m", await Numeri());

        // Una revisione pubblicata non si tocca mai, nemmeno se era automatica.
        (_, token) = await Salva("cinque", automatico: true, token);
        Assert.Equal("1m,2m,5m,6a", await Numeri());
        await c.PubblicaAsync(percorso);
        token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        (_, token) = await Salva("sei", automatico: true, token);
        Assert.Equal("1m,2m,5m,6a,7a", await Numeri());
        using var pubblica = await c.Web.SessioneA().GetAsync("/auto");
        Assert.Contains("cinque", await pubblica.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Un altro autore non sostituisce l'automatico di un collega.
        var collega = await c.NuovoAmministratoreAsync("carla@esempio.it");
        (_, token) = await Salva("sette", automatico: true, token, collega);
        Assert.Equal("1m,2m,5m,6a,7a,8a", await Numeri());
        Assert.Equal(SerializzatoreDocumento.Scrivi(_registro.Normalizza(Doc("sette"))), (await c.RevisioniAsync(id))[^1].DocumentoRenderJson);
    }

    [Fact]
    public async Task IlBuilderNonSiApreNeSiSalvaFuoriDalProprioTenantEFuoriDalleRegole()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, _) = await c.CreaPaginaAsync("pagina-libera", "mia");
        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        var semplice = await c.CreaPaginaSempliceAsync();

        // Anonimo: reindirizzato all'accesso, sia la pagina sia il salvataggio.
        var anonimo = c.Web.SessioneA();
        using var paginaAnonima = await anonimo.GetAsync($"/admin/pagine/{id}/builder");
        Assert.True(SessioneProva.EReindirizzamento(paginaAnonima));
        using var salvaAnonimo = await anonimo.InviaJsonAsync($"/admin/accesso", $"/admin/pagine/{id}/builder?handler=Salva", Corpo(Progetto(Iniziale("pagina-libera")), token), conToken: false);
        Assert.True(SessioneProva.EReindirizzamento(salvaAnonimo));

        // Un amministratore di un altro tenant non vede nemmeno la pagina (404) e non può salvare.
        var altro = c.Web.SessioneB();
        await altro.AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);
        using var paginaAltrui = await altro.GetAsync($"/admin/pagine/{id}/builder");
        Assert.Equal(HttpStatusCode.NotFound, paginaAltrui.StatusCode);
        using var statoAltrui = await altro.GetAsync($"/admin/pagine/{id}/builder?handler=Stato");
        Assert.Equal(HttpStatusCode.NotFound, statoAltrui.StatusCode);
        using var salvaAltrui = await altro.InviaJsonAsync("/admin/pagine", $"/admin/pagine/{id}/builder?handler=Salva", Corpo(Progetto(Iniziale("pagina-libera")), token));
        Assert.Equal(HttpStatusCode.NotFound, salvaAltrui.StatusCode);

        // Senza il token antiforgery la richiesta è rifiutata dal framework.
        using var senzaToken = await c.Sessione.InviaJsonAsync("/admin/pagine", $"/admin/pagine/{id}/builder?handler=Salva", Corpo(Progetto(Iniziale("pagina-libera")), token), conToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, senzaToken.StatusCode);

        // Una pagina che non usa il builder non si apre e non accetta salvataggi.
        using var apri = await c.Sessione.GetAsync($"/admin/pagine/{semplice}/builder");
        Assert.Equal(HttpStatusCode.NotFound, apri.StatusCode);
        using var salvaSemplice = await c.SalvaAsync(semplice, token, Progetto(Iniziale("pagina-libera")));
        Assert.Equal(HttpStatusCode.Conflict, salvaSemplice.StatusCode);

        // Corpo non JSON, token mancante o spazzatura: errore, nessuna revisione.
        using var vuoto = await c.Sessione.InviaJsonAsync("/admin/pagine", $"/admin/pagine/{id}/builder?handler=Salva", "{}");
        Assert.Equal(HttpStatusCode.BadRequest, vuoto.StatusCode);
        using var tokenSbagliato = await c.SalvaAsync(id, "non-base64!", Progetto(Iniziale("pagina-libera")));
        Assert.Equal(HttpStatusCode.BadRequest, tokenSbagliato.StatusCode);
        using var troppoGrande = await c.SalvaAsync(id, token, "{\"x\":\"" + new string('a', AdapterGrapesJs.ByteMassimiProgetto + 100_000) + "\"}");
        Assert.True(troppoGrande.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge);
        Assert.Single(await c.RevisioniAsync(id));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage risposta) =>
        JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static string Estrai(string html, string inizio, string fine)
    {
        var i = html.IndexOf(inizio, StringComparison.Ordinal);
        var f = html.IndexOf(fine, i, StringComparison.Ordinal);
        return html[i..(f + fine.Length)];
    }

    [GeneratedRegex("name=\"TokenConcorrenza\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenConcorrenza();

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, SessioneProva sessione, Guid adminA)
        {
            Web = web;
            Sessione = sessione;
            AdminA = adminA;
        }

        public ScenarioWeb Web { get; }

        public SessioneProva Sessione { get; }

        public Guid AdminA { get; }

        public static async Task<Contesto> CreaAsync()
        {
            var web = await ScenarioWeb.CreaAsync();
            var admin = await web.CreaTenantAdminAsync("anna@esempio.it");
            await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, DvCms.Models.Accessi.RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            var sessione = web.SessioneA();
            await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
            return new Contesto(web, sessione, admin);
        }

        public async Task<SessioneProva> NuovoAmministratoreAsync(string email)
        {
            await Web.CreaTenantAdminAsync(email);
            var sessione = Web.SessioneA();
            await sessione.AccediAsync(email, DatiProva.PasswordProva);
            return sessione;
        }

        public async Task<(Guid Id, string Percorso)> CreaPaginaAsync(string modello, string titolo, string? slug = null)
        {
            using var creazione = await Sessione.InviaModuloAsync(
                $"/admin/pagine/nuova?modello={modello}",
                [new("Titolo", titolo), new("Slug", slug ?? string.Empty), new("CodiceLingua", "it")]);
            Assert.True(SessioneProva.EReindirizzamento(creazione), "creazione non riuscita");
            var percorso = SessioneProva.Destinazione(creazione)!;
            return (Guid.Parse(percorso[(percorso.LastIndexOf('/') + 1)..]), percorso);
        }

        public async Task<Guid> CreaPaginaSempliceAsync()
        {
            var (id, _) = await CreaPaginaAsync("testo-semplice", "Semplice");
            return id;
        }

        public async Task<JsonElement> StatoAsync(Guid id)
        {
            using var risposta = await Sessione.GetAsync($"/admin/pagine/{id}/builder?handler=Stato");
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            return await Json(risposta);
        }

        public Task<HttpResponseMessage> SalvaAsync(Guid id, string token, string progetto, bool automatico = false, SessioneProva? sessione = null) =>
            (sessione ?? Sessione).InviaJsonAsync("/admin/pagine", $"/admin/pagine/{id}/builder?handler=Salva", Corpo(progetto, token, automatico));

        public async Task<string> TokenModuloAsync(string percorso)
        {
            using var pagina = await Sessione.GetAsync(percorso);
            return WebUtility.HtmlDecode(TokenConcorrenza().Match(await pagina.Content.ReadAsStringAsync(Ct)).Groups[1].Value);
        }

        public async Task PubblicaAsync(string percorso)
        {
            var token = await TokenModuloAsync(percorso);
            using var risposta = await Sessione.InviaModuloAsync(percorso, [new("TokenConcorrenza", token)], $"{percorso}?handler=Pubblica");
            Assert.True(SessioneProva.EReindirizzamento(risposta));
        }

        public async Task ImpostaNegozioAsync(Guid piattaforma, bool attivo, DatiProva.TenantCreato? tenant = null)
        {
            tenant ??= Web.A;
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(piattaforma, tenant.IdTenant, tenant.IdSito, attivo, Ct)).Riuscito);
        }

        public async Task<Guid> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal prezzo, StatoProdotto stato = StatoProdotto.Pubblicato, Guid? categoria = null, string? descrizione = null)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var dati = new DatiProdotto(null, null, stato, 22m, [new TraduzioneProdottoDati("it", nome, descrizione)], [new VarianteDati(null, "SKU-" + Guid.NewGuid().ToString("N")[..8], prezzo, null, true, null)], null, categoria is { } cat ? [cat] : null);
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : await IdAdminBAsync();
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioCatalogo>().SalvaAsync(attore, dati, Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public async Task<Guid> CreaCategoriaAsync(DatiProva.TenantCreato tenant, string nome)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : await IdAdminBAsync();
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioCategorie>().SalvaAsync(attore, new DatiCategoria(null, 0, [new NomeCategoriaDati("it", nome)]), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public async Task<IReadOnlyList<RevisionePagina>> RevisioniAsync(Guid idPagina)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RevisioniPagina.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.IdPagina == idPagina).OrderBy(r => r.NumeroRevisione).ToListAsync(Ct);
        }

        public async Task ImpostaVersioneModelloAsync(Guid idPagina, int versione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RevisioniPagina.IgnoreQueryFilters().Where(r => r.IdPagina == idPagina)
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.VersioneModello, versione), Ct);
        }

        public async Task ScriviDocumentoDirettoAsync(Guid idPagina, string json)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            await contesto.RevisioniPagina.IgnoreQueryFilters().Where(r => r.IdPagina == idPagina)
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.DocumentoRenderJson, json), Ct);
            ambito.ServiceProvider.GetRequiredService<IInvalidazioneCache>().Registra(Web.A.IdTenant, Web.A.IdSito);
            await contesto.SaveChangesAsync(Ct);
        }

        public async Task<Guid> CaricaAsync(DatiProva.TenantCreato tenant, DvCms.Models.Media.VisibilitaMedia visibilita = DvCms.Models.Media.VisibilitaMedia.Pubblica)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : await IdAdminBAsync();
            var media = ambito.ServiceProvider.GetRequiredService<IServizioMedia>();
            var esito = await media.CaricaAsync(attore, new FileDaCaricare(new MemoryStream(ImmaginiProva.Png(8, 8)), "foto.png", "image/png"), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            if (visibilita != DvCms.Models.Media.VisibilitaMedia.Pubblica)
            {
                Assert.True((await media.ImpostaVisibilitaAsync(attore, esito.Id!.Value, visibilita, Ct)).Riuscito);
            }

            return esito.Id!.Value;
        }

        public async Task ImpostaTemaAsync(string chiave)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(Web.A.IdTenant, Web.A.IdSito, Web.A.IdDominio, Web.A.Host, false));
            var temi = ambito.ServiceProvider.GetRequiredService<IServizioTemi>();
            var token = (await temi.LeggiAsync(AdminA, Ct)).TokenConcorrenza;
            Assert.True((await temi.SalvaAsync(AdminA, chiave, new Dictionary<string, string>(), token, Ct)).Riuscito);
        }

        public async ValueTask DisposeAsync() => await Web.DisposeAsync();

        private async Task<Guid> IdAdminBAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters()
                .Where(u => u.EmailNormalizzata == "bruno@esempio.it").Select(u => u.Id).SingleAsync(Ct);
        }
    }
}

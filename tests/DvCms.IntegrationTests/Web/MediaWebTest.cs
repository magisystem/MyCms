using System.Net;
using System.Security.Cryptography;
using System.Text;
using DvCms.Application.Media;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Media;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Libreria media (D-003, D-004): caricamento verificato dal server, archivio per tenant, download autorizzati.</summary>
public class MediaWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlCaricamentoDalModuloRegistraMetadatiVerificatiELiSalvaSottoUnaChiaveDelServer()
    {
        await using var c = await Contesto.CreaAsync();
        var sessione = await c.AccediAsync();
        var png = ImmaginiProva.Png(640, 480);

        using var risposta = await sessione.InviaFileAsync("/admin/media", "file", "Logo Azienda.PNG", png, "image/gif", "/admin/media?handler=Carica");

        Assert.True(SessioneProva.EReindirizzamento(risposta), await risposta.Content.ReadAsStringAsync(Ct));
        Assert.Matches("^/admin/media/[0-9a-f-]{36}$", SessioneProva.Destinazione(risposta));
        var risorsa = Assert.Single(await c.RisorseAsync());
        Assert.Equal("Logo Azienda.PNG", risorsa.NomeOriginale);
        Assert.Equal("image/png", risorsa.TipoContenuto);
        Assert.Equal(png.Length, risorsa.Dimensione);
        Assert.Equal(SHA256.HashData(png), risorsa.HashSha256);
        Assert.Equal((640, 480), (risorsa.Larghezza, risorsa.Altezza));
        Assert.Equal(VisibilitaMedia.Pubblica, risorsa.Visibilita);
        Assert.Equal(StatoScansioneMedia.NonEseguita, risorsa.StatoScansione);
        Assert.Equal($"{c.Web.A.IdTenant:N}/{c.Web.A.IdSito:N}/{risorsa.Id:N}", risorsa.ChiaveOggetto);
        Assert.True(File.Exists(c.PercorsoOggetto(risorsa)));
        Assert.Equal(png, await File.ReadAllBytesAsync(c.PercorsoOggetto(risorsa), Ct));
    }

    [Fact]
    public async Task UnaRisorsaPubblicaSiScaricaDalDominioConLeIntestazioniSicure()
    {
        await using var c = await Contesto.CreaAsync();
        var png = ImmaginiProva.Png(10, 10);
        var id = await c.CaricaAsync(c.Web.A, "foto.png", png);

        using var risposta = await c.Web.SessioneA().GetAsync($"/media/{id}");

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal("image/png", risposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await risposta.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("nosniff", risposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; sandbox", risposta.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("inline", risposta.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("public, max-age=3600", risposta.Headers.CacheControl?.ToString());
        var etag = risposta.Headers.ETag?.Tag;
        Assert.Equal($"\"{Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()}\"", etag);

        using var condizionata = new HttpRequestMessage(HttpMethod.Get, $"/media/{id}");
        condizionata.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var nonModificata = await c.Web.SessioneA().InviaAsync(condizionata);
        Assert.Equal(HttpStatusCode.NotModified, nonModificata.StatusCode);

        using var testa = new HttpRequestMessage(HttpMethod.Head, $"/media/{id}");
        using var rispostaTesta = await c.Web.SessioneA().InviaAsync(testa);
        Assert.Equal(HttpStatusCode.OK, rispostaTesta.StatusCode);
    }

    [Fact]
    public async Task IlTipoDichiaratoDalClientNonContaMaSoloLaFirmaDelContenuto()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CaricaAsync(c.Web.A, "foto.jpg", ImmaginiProva.Jpeg(20, 10), tipoDichiarato: "text/html");

        using var risposta = await c.Web.SessioneA().GetAsync($"/media/{id}");

        Assert.Equal("image/jpeg", risposta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnaRisorsaDiUnAltroTenantOInesistenteNonSiScaricaDalDominio()
    {
        await using var c = await Contesto.CreaAsync();
        var idA = await c.CaricaAsync(c.Web.A, "a.png", ImmaginiProva.Png(5, 5));
        var idB = await c.CaricaAsync(c.Web.B, "b.png", ImmaginiProva.Png(5, 5));

        using var daB = await c.Web.SessioneB().GetAsync($"/media/{idA}");
        using var daA = await c.Web.SessioneA().GetAsync($"/media/{idB}");
        using var inesistente = await c.Web.SessioneA().GetAsync($"/media/{Guid.NewGuid()}");
        using var propria = await c.Web.SessioneA().GetAsync($"/media/{idA}");

        Assert.Equal(HttpStatusCode.NotFound, daB.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, daA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inesistente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, propria.StatusCode);
    }

    [Fact]
    public async Task UnaRisorsaPrivataSiVedeSoloDaglIAmministratoriDelTenant()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CaricaAsync(c.Web.A, "riservata.png", ImmaginiProva.Png(5, 5));
        var sessione = await c.AccediAsync();
        using var rendiPrivata = await sessione.InviaModuloAsync($"/admin/media/{id}", [new("visibilita", "Privata")], $"/admin/media/{id}?handler=Visibilita");
        Assert.True(SessioneProva.EReindirizzamento(rendiPrivata));

        using var pubblica = await c.Web.SessioneA().GetAsync($"/media/{id}");
        using var admin = await sessione.GetAsync($"/admin/media/{id}?handler=File");
        using var anonimo = await c.Web.SessioneA().GetAsync($"/admin/media/{id}?handler=File");
        using var altroTenant = await (await c.AccediAltroTenantAsync()).GetAsync($"/admin/media/{id}?handler=File");

        Assert.Equal(HttpStatusCode.NotFound, pubblica.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("nosniff", admin.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", admin.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(anonimo));
        Assert.Equal(HttpStatusCode.NotFound, altroTenant.StatusCode);
    }

    [Fact]
    public async Task UnaRisorsaInQuarantenaNonSiServeANessunoDalSito()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CaricaAsync(c.Web.A, "sospetta.png", ImmaginiProva.Png(5, 5));
        await c.ImpostaStatoAsync(id, StatoScansioneMedia.InQuarantena);

        using var risposta = await c.Web.SessioneA().GetAsync($"/media/{id}");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    public static TheoryData<string, byte[], string> FileNonAmmessi => new()
    {
        { "pagina.html", "<html><script>alert(1)</script></html>"u8.ToArray(), "Tipo" },
        { "disegno.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>"u8.ToArray(), "Tipo" },
        { "finta.png", "<html><script>alert(1)</script></html>"u8.ToArray(), "Tipo" },
        { "finta.jpg", "GIF89a testo qualsiasi"u8.ToArray(), "Tipo" },
        { "scambiata.jpg", ImmaginiProva.Png(10, 10), "Tipo" },
        { "scambiata.png", ImmaginiProva.Jpeg(10, 10), "Tipo" },
        { "senza-estensione", ImmaginiProva.Png(10, 10), "Tipo" },
        { "doppia.png.exe", ImmaginiProva.Png(10, 10), "Tipo" },
        { "documento.pdf", "%PDF-1.4"u8.ToArray(), "Tipo" },
        { "troppo-grande.png", ImmaginiProva.Png(20_000, 100), "Dimensioni" },
        { "troppi-pixel.png", ImmaginiProva.Png(10_000, 10_000), "Dimensioni" },
        { "vuota.png", [], "File" },
    };

    [Theory]
    [MemberData(nameof(FileNonAmmessi))]
    public async Task IFileNonAmmessiSonoRifiutatiENonLasciaNulla(string nome, byte[] contenuto, string campo)
    {
        await using var c = await Contesto.CreaAsync();

        var esito = await c.CaricaDirettoAsync(c.Web.A, nome, contenuto, "image/png");

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal([campo], esito.CampiNonValidi);
        Assert.Empty(await c.RisorseAsync());
        Assert.False(Directory.Exists(c.Archivio) && Directory.EnumerateFiles(c.Archivio, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task UnFileOltreIlLimiteDiDimensioneERifiutatoSenzaLeggerloTutto()
    {
        await using var c = await Contesto.CreaAsync(new Dictionary<string, string?> { ["Media:DimensioneMassimaKb"] = "1" });
        var grande = ImmaginiProva.Png(10, 10).Concat(new byte[2048]).ToArray();

        var esito = await c.CaricaDirettoAsync(c.Web.A, "grande.png", grande, "image/png");
        var entro = await c.CaricaDirettoAsync(c.Web.A, "piccola.png", ImmaginiProva.Png(10, 10), "image/png");

        Assert.Equal(["Dimensione"], esito.CampiNonValidi);
        Assert.True(entro.Riuscito, entro.Codice.ToString());
        Assert.Single(await c.RisorseAsync());
    }

    [Fact]
    public async Task IlNomeDelFileEPulitoDaPercorsiECaratteriDiControlloENonInfluisceSullaChiave()
    {
        await using var c = await Contesto.CreaAsync();

        var esito = await c.CaricaDirettoAsync(c.Web.A, "..\\..\\etc/passwd/<i>x\u0007y.png", ImmaginiProva.Png(8, 8), "image/png");

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        var risorsa = Assert.Single(await c.RisorseAsync());
        Assert.Equal("<i>xy.png", risorsa.NomeOriginale);
        Assert.DoesNotContain("passwd", risorsa.ChiaveOggetto, StringComparison.Ordinal);

        // Nell'elenco il nome è mostrato codificato.
        var sessione = await c.AccediAsync();
        var html = await (await sessione.GetAsync("/admin/media")).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("<i>xy.png", html, StringComparison.Ordinal);
        Assert.Contains("&lt;i&gt;xy.png", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlTestoAlternativoSiImpostaPerLinguaSiToglieEUsaIlPredefinitoNellElenco()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CaricaAsync(c.Web.A, "foto.png", ImmaginiProva.Png(10, 10));

        var it = await c.ComeAsync(c.Web.A, (s, a) => s.ImpostaTestoAlternativoAsync(a, id, "it", "  Un gatto  ", Ct));
        var en = await c.ComeAsync(c.Web.A, (s, a) => s.ImpostaTestoAlternativoAsync(a, id, "EN", "A cat", Ct));

        Assert.True(it.Riuscito && en.Riuscito);
        var dettaglio = (await c.ComeAsync(c.Web.A, (s, a) => s.LeggiAsync(a, id, Ct)))!;
        Assert.Equal([("it", "Un gatto"), ("en", "A cat")], dettaglio.TestiAlternativi.Select(t => (t.CodiceLingua, t.Testo)));
        Assert.Equal("Un gatto", Assert.Single(await c.ComeAsync(c.Web.A, (s, a) => s.ElencaAsync(a, Ct))).TestoAlternativoPredefinito);

        var toglie = await c.ComeAsync(c.Web.A, (s, a) => s.ImpostaTestoAlternativoAsync(a, id, "it", "   ", Ct));
        Assert.True(toglie.Riuscito);
        Assert.Null(Assert.Single(await c.ComeAsync(c.Web.A, (s, a) => s.ElencaAsync(a, Ct))).TestoAlternativoPredefinito);
        Assert.Equal(1, await c.ContaTestiAsync());
    }

    [Theory]
    [InlineData("it", 501, "TestoAlternativo")]
    [InlineData("it", -1, "TestoAlternativo")]
    [InlineData("fr", 5, "Lingua")]
    [InlineData("", 5, "Lingua")]
    public async Task IlTestoAlternativoNonValidoOInUnaLinguaNonAbilitataERifiutato(string lingua, int lunghezza, string campo)
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CaricaAsync(c.Web.A, "foto.png", ImmaginiProva.Png(10, 10));
        var testo = lunghezza < 0 ? "riga\nsu due" : new string('x', lunghezza);

        var esito = await c.ComeAsync(c.Web.A, (s, a) => s.ImpostaTestoAlternativoAsync(a, id, lingua, testo, Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal([campo], esito.CampiNonValidi);
        Assert.Equal(0, await c.ContaTestiAsync());
    }

    [Fact]
    public async Task LEliminazioneTogliMetadatiTestiEOggetto()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CaricaAsync(c.Web.A, "foto.png", ImmaginiProva.Png(10, 10));
        await c.ComeAsync(c.Web.A, (s, a) => s.ImpostaTestoAlternativoAsync(a, id, "it", "Testo", Ct));
        var percorso = c.PercorsoOggetto((await c.RisorseAsync()).Single());

        var esito = await c.ComeAsync(c.Web.A, (s, a) => s.EliminaAsync(a, id, Ct));

        Assert.True(esito.Riuscito);
        Assert.Empty(await c.RisorseAsync());
        Assert.Equal(0, await c.ContaTestiAsync());
        Assert.False(File.Exists(percorso));
        using var risposta = await c.Web.SessioneA().GetAsync($"/media/{id}");
        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task UnAmministratoreDiUnAltroTenantNonVedeNeModificaNeEliminaLeRisorse()
    {
        await using var c = await Contesto.CreaAsync();
        var idB = await c.CaricaAsync(c.Web.B, "b.png", ImmaginiProva.Png(10, 10));

        // L'amministratore di A, sul proprio host, non raggiunge la risorsa di B né con l'identificativo né da attore sbagliato.
        var leggi = await c.ComeAsync(c.Web.A, (s, a) => s.LeggiAsync(a, idB, Ct));
        var elimina = await c.ComeAsync(c.Web.A, (s, a) => s.EliminaAsync(a, idB, Ct));
        var testo = await c.ComeAsync(c.Web.A, (s, a) => s.ImpostaTestoAlternativoAsync(a, idB, "it", "x", Ct));
        var visibilita = await c.ComeAsync(c.Web.A, (s, a) => s.ImpostaVisibilitaAsync(a, idB, VisibilitaMedia.Privata, Ct));
        var apri = await c.ComeAsync(c.Web.A, (s, a) => s.ApriPerAmministratoreAsync(a, idB, Ct));
        var attoreSbagliato = await c.ComeAsync(c.Web.A, (s, _) => s.EliminaAsync(c.AdminB, idB, Ct));
        var caricaComeB = await c.ComeAsync(c.Web.A, (s, _) => s.CaricaAsync(c.AdminB, new FileDaCaricare(new MemoryStream(ImmaginiProva.Png(5, 5)), "x.png", "image/png"), Ct));

        Assert.Null(leggi);
        Assert.Null(apri);
        Assert.Equal(CodiceEsito.NonTrovato, elimina.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, testo.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, visibilita.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, attoreSbagliato.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, caricaComeB.Codice);
        Assert.Single(await c.RisorseAsync());
        Assert.Single(Directory.EnumerateFiles(c.Archivio, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LeSchermateMediaRichiedonoLaSessioneAdmin()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CaricaAsync(c.Web.A, "foto.png", ImmaginiProva.Png(10, 10));

        using var elenco = await c.Web.SessioneA().GetAsync("/admin/media");
        using var dettaglio = await c.Web.SessioneA().GetAsync($"/admin/media/{id}");

        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(elenco), StringComparison.Ordinal);
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(dettaglio), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnProviderDiArchivioSconosciutoFermaLAvvio()
    {
        await using var database = await DatabaseDiProva.CreaAsync(Ct);

        var errore = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ServiziProva.CreaConMigrazioniAsync(database.Connessione, new Dictionary<string, string?> { ["Archivio:Provider"] = "Nuvola" }, annullamento: Ct));

        Assert.Contains("Nuvola", errore.Message, StringComparison.Ordinal);
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, Guid adminA, Guid adminB)
        {
            Web = web;
            AdminA = adminA;
            AdminB = adminB;
        }

        public ScenarioWeb Web { get; }

        public Guid AdminA { get; }

        public Guid AdminB { get; }

        public string Archivio => Web.Fabbrica.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Archivio:Percorso"]!;

        public static async Task<Contesto> CreaAsync(IReadOnlyDictionary<string, string?>? impostazioni = null)
        {
            var web = await ScenarioWeb.CreaAsync(impostazioni: impostazioni);
            var adminA = await web.CreaTenantAdminAsync("anna@esempio.it");
            var adminB = await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            return new Contesto(web, adminA, adminB);
        }

        public string PercorsoOggetto(RisorsaMedia risorsa) => Path.Combine(Archivio, risorsa.ChiaveOggetto.Replace('/', Path.DirectorySeparatorChar));

        public async Task<SessioneProva> AccediAsync()
        {
            var sessione = Web.SessioneA();
            await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
            return sessione;
        }

        public async Task<SessioneProva> AccediAltroTenantAsync()
        {
            var sessione = Web.SessioneB();
            await sessione.AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);
            return sessione;
        }

        public async Task<Guid> CaricaAsync(DatiProva.TenantCreato tenant, string nome, byte[] contenuto, string tipoDichiarato = "image/png")
        {
            var esito = await CaricaDirettoAsync(tenant, nome, contenuto, tipoDichiarato);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public Task<EsitoOperazione> CaricaDirettoAsync(DatiProva.TenantCreato tenant, string nome, byte[] contenuto, string tipoDichiarato) =>
            ComeAsync(tenant, (servizio, attore) => servizio.CaricaAsync(attore, new FileDaCaricare(new MemoryStream(contenuto), nome, tipoDichiarato), Ct));

        public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioMedia, Guid, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : AdminB;
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioMedia>(), attore);
        }

        public async Task<IReadOnlyList<RisorsaMedia>> RisorseAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RisorseMedia.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        }

        public async Task<int> ContaTestiAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TraduzioniRisorseMedia.IgnoreQueryFilters().CountAsync(Ct);
        }

        public async Task ImpostaStatoAsync(Guid id, StatoScansioneMedia stato)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RisorseMedia.IgnoreQueryFilters()
                .Where(r => r.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(r => r.StatoScansione, stato), Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}

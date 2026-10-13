using System.Net;
using System.Text;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Esportazione CSV degli ordini: accesso, formati, filtri, neutralizzazione delle formule, isolamento fra tenant e audit.</summary>
public class EsportazioneOrdiniTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FiltroEsportazioneOrdini Tutti(FormatoEsportazioneOrdini formato = FormatoEsportazioneOrdini.Ordini) => new(null, null, null, formato);

    private static string Testo(EsportazioneOrdini e) => Encoding.UTF8.GetString(e.Contenuto).TrimStart('﻿');

    private static async Task<Guid> OrdinaAsync(ContestoNegozio c, DatiProva.TenantCreato tenant, Guid cliente, Guid variante, Guid ritiro, decimal totale, int quantita = 1)
    {
        await c.AggiungiAsync(cliente, variante, quantita, tenant);
        var esito = await c.ComeAsync(tenant, s => s.CreaDaCarrelloAsync(cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, totale), Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        return esito.Id!.Value;
    }

    private static async Task AggiornaOrdineAsync(ContestoNegozio c, Guid id, Action<Ordine> modifica)
    {
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var ordine = await contesto.Ordini.IgnoreQueryFilters().SingleAsync(o => o.Id == id, Ct);
        modifica(ordine);
        await contesto.SaveChangesAsync(Ct);
    }

    private static async Task<(ContestoNegozio C, Guid Primo, Guid Secondo)> ScenarioAsync()
    {
        var c = await ContestoNegozio.CreaAsync();
        var maglietta = await c.CreaProdottoAsync(c.A, "Maglietta", 22m, ("TS-M", 19.90m, "Taglia M"));
        var libro = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 12.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        var primo = await OrdinaAsync(c, c.A, c.Cliente, maglietta[0], ritiro, 59.70m, 3);
        var secondo = await OrdinaAsync(c, c.A, c.AltroCliente, libro[0], ritiro, 12.00m);
        return (c, primo, secondo);
    }

    [Fact]
    public async Task SoloLAmministrazioneDelTenantEsporta()
    {
        var (c, _, _) = await ScenarioAsync();
        await using var _ = c;

        foreach (var attore in new[] { c.Cliente, c.AdminB, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(attore, Tutti(), Ct)));
        }

        Assert.NotEmpty((await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.AdminA, Tutti(), Ct))).Contenuto);
        Assert.NotEmpty((await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.Platform, Tutti(), Ct))).Contenuto);
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var negati = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata, Ct);
        Assert.True(negati >= 2);
    }

    [Fact]
    public async Task UnaRigaPerOrdineConTotaliConLaVirgolaEIlBomPerExcel()
    {
        var (c, _, _) = await ScenarioAsync();
        await using var _ = c;

        var esportazione = await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.AdminA, Tutti(), Ct));

        Assert.Equal([0xEF, 0xBB, 0xBF], esportazione.Contenuto[..3]);
        Assert.StartsWith("ordini-", esportazione.NomeFile, StringComparison.Ordinal);
        Assert.EndsWith(".csv", esportazione.NomeFile, StringComparison.Ordinal);
        Assert.False(esportazione.Troncata);
        Assert.Equal(2, esportazione.Righe);
        var righe = Testo(esportazione).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, righe.Length);
        Assert.StartsWith("Numero;DataUtc;Stato;Valuta;TotaleLordo;", righe[0], StringComparison.Ordinal);
        var primo = righe[1].Split(';');
        Assert.Equal(("1", "InAttesaDiPagamento", "EUR", "59,70"), (primo[0], primo[2], primo[3], primo[4]));
        Assert.Equal("cliente@esempio.it", primo[10]);
        Assert.StartsWith("2;", righe[2], StringComparison.Ordinal);
        Assert.Contains("12,00", righe[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlFormatoRigheHaUnaRigaPerArticoloPiuLaSpedizione()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var maglietta = await c.CreaProdottoAsync(c.A, "Maglietta", 22m, ("TS-M", 19.90m, "Taglia M"));
        var libro = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 12.00m, null));
        var corriere = await c.CreaMetodoAsync(c.A, "Corriere Italia", 6.90m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, maglietta[0], 3);
        await c.AggiungiAsync(c.Cliente, libro[0], 1);
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(corriere, "Mario Rossi", "Via Roma 1", null, "20100", "Milano", "IT", 78.60m), Ct));
        Assert.True(esito.Riuscito);

        var esportazione = await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.AdminA, Tutti(FormatoEsportazioneOrdini.Righe), Ct));

        Assert.StartsWith("righe-ordini-", esportazione.NomeFile, StringComparison.Ordinal);
        Assert.Equal(3, esportazione.Righe);
        var righe = Testo(esportazione).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var maglia = righe[1].Split(';');
        Assert.Equal(("1", "TS-M", "Maglietta", "Taglia M", "3", "19,90", "22,00", "59,70", "48,93", "10,77"), (maglia[3], maglia[4], maglia[5], maglia[6], maglia[7], maglia[8], maglia[9], maglia[10], maglia[11], maglia[12]));
        Assert.Contains(";LIB-1;Libro;;1;12,00;4,00;12,00;", righe[2], StringComparison.Ordinal);
        var spedizione = righe[3].Split(';');
        Assert.Equal(("Spedizione", "Corriere Italia", "6,90", "5,66", "1,24"), (spedizione[3], spedizione[5], spedizione[10], spedizione[11], spedizione[12]));
    }

    [Fact]
    public async Task IFiltriPerDataEStatoRestringonoLElenco()
    {
        var (c, primo, secondo) = await ScenarioAsync();
        await using var _ = c;
        await AggiornaOrdineAsync(c, primo, o => { o.CreatoIlUtc = new DateTime(2026, 3, 10, 23, 59, 0, DateTimeKind.Utc); o.Stato = StatoOrdine.Pagato; });
        await AggiornaOrdineAsync(c, secondo, o => o.CreatoIlUtc = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc));

        async Task<string[]> NumeriAsync(FiltroEsportazioneOrdini filtro) =>
            [.. Testo(await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.AdminA, filtro, Ct))).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(r => r.Split(';')[0])];

        Assert.Equal(["1"], await NumeriAsync(Tutti() with { Al = new DateOnly(2026, 3, 10) }));
        Assert.Equal(["2"], await NumeriAsync(Tutti() with { Dal = new DateOnly(2026, 3, 11) }));
        Assert.Equal(["1", "2"], await NumeriAsync(Tutti() with { Dal = new DateOnly(2026, 3, 10), Al = new DateOnly(2026, 3, 11) }));
        Assert.Equal(["1"], await NumeriAsync(Tutti() with { Stato = StatoOrdine.Pagato }));
        Assert.Empty(await NumeriAsync(Tutti() with { Stato = StatoOrdine.Rimborsato }));
        Assert.Empty(await NumeriAsync(Tutti() with { Dal = new DateOnly(2027, 1, 1) }));
    }

    [Fact]
    public async Task IlTestoLiberoNonDiventaUnaFormulaNeRompeIlFile()
    {
        var (c, primo, _) = await ScenarioAsync();
        await using var _ = c;
        await AggiornaOrdineAsync(c, primo, o =>
        {
            o.NomeAcquirente = "=HYPERLINK(\"http://evil.example\";\"clic\")";
            o.Destinatario = "Rossi; \"Mario\"\nVia Roma";
            o.EmailAcquirente = "+cmd@esempio.it";
        });

        var testo = Testo(await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.AdminA, Tutti(), Ct)));

        Assert.Contains("\"'=HYPERLINK(\"\"http://evil.example\"\";\"\"clic\"\")\"", testo, StringComparison.Ordinal);
        Assert.Contains("\"Rossi; \"\"Mario\"\"\nVia Roma\"", testo, StringComparison.Ordinal);
        Assert.Contains(";'+cmd@esempio.it;", testo, StringComparison.Ordinal);
        Assert.DoesNotContain(";=HYPERLINK", testo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonEsportaMaGliOrdiniDiUnAltroTenant()
    {
        var (c, _, _) = await ScenarioAsync();
        await using var _ = c;
        var prodotto = await c.CreaProdottoAsync(c.B, "Spilla", 22m, ("SP-1", 5m, null));
        var ritiro = await c.CreaRitiroAsync(c.B);
        await OrdinaAsync(c, c.B, c.ClienteDiB, prodotto[0], ritiro, 5m);

        var daA = Testo(await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.AdminA, Tutti(FormatoEsportazioneOrdini.Righe), Ct)));
        var daB = Testo(await c.ComeEsportazioneAsync(c.B, s => s.EsportaAsync(c.AdminB, Tutti(FormatoEsportazioneOrdini.Righe), Ct)));

        Assert.DoesNotContain("SP-1", daA, StringComparison.Ordinal);
        Assert.Contains("SP-1", daB, StringComparison.Ordinal);
        Assert.DoesNotContain("TS-M", daB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LAuditRegistraFiltroEConteggioMaNonIDatiPersonali()
    {
        var (c, _, _) = await ScenarioAsync();
        await using var _ = c;

        await c.ComeEsportazioneAsync(c.A, s => s.EsportaAsync(c.AdminA, Tutti() with { Stato = StatoOrdine.InAttesaDiPagamento }, Ct));

        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var voce = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().SingleAsync(a => a.Azione == AzioniAudit.EsportazioneOrdini, Ct);
        Assert.Equal(c.AdminA, voce.IdUtenzaAttore);
        Assert.Equal(c.A.IdTenant, voce.IdTenant);
        Assert.Contains("righe 2", voce.Dettagli, StringComparison.Ordinal);
        Assert.Contains("stato InAttesaDiPagamento", voce.Dettagli, StringComparison.Ordinal);
        Assert.DoesNotContain("@", voce.Dettagli, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaPaginaScaricaIlCsvSoloAgliAmministratoriEScartaIParametriNonValidi()
    {
        var (c, _, _) = await ScenarioAsync();
        await using var _ = c;
        var anna = c.Web.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var anonima = c.Web.SessioneA();

        using var senzaSessione = await anonima.GetAsync("/admin/negozio/ordini?handler=Esporta");
        using var valida = await anna.GetAsync("/admin/negozio/ordini?handler=Esporta&formato=Righe&stato=InAttesaDiPagamento");
        using var dataErrata = await anna.GetAsync("/admin/negozio/ordini?handler=Esporta&dal=31/12/2026");
        using var invertite = await anna.GetAsync("/admin/negozio/ordini?handler=Esporta&dal=2026-05-02&al=2026-05-01");

        Assert.True(SessioneProva.EReindirizzamento(senzaSessione));
        Assert.Contains("/admin/accesso", SessioneProva.Destinazione(senzaSessione), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, valida.StatusCode);
        Assert.Equal("text/csv", valida.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", valida.Content.Headers.ContentDisposition?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("righe-ordini-", valida.Content.Headers.ContentDisposition?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("no-store", string.Join(',', valida.Headers.CacheControl?.ToString() ?? string.Empty), StringComparison.Ordinal);
        Assert.Contains("TS-M", await valida.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(dataErrata));
        Assert.True(SessioneProva.EReindirizzamento(invertite));
    }
}

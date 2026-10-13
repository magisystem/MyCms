using System.Net;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Schermata della coda dell'outbox nel menu Platform (G-001).</summary>
public class CodaWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlPlatformAdminVedeIFallitiConLUltimoErroreELiRimetteInCoda()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var id = await InserisciFallitoAsync(scenario, "SmtpException: <b>server</b> non raggiungibile");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        var html = await (await sessione.GetAsync("/admin/platform/coda")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("InvioEmail", html, StringComparison.Ordinal);
        Assert.Contains("SmtpException: &lt;b&gt;server&lt;/b&gt; non raggiungibile", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>server</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PAYLOAD-RISERVATO", html, StringComparison.Ordinal);

        using var riprova = await sessione.InviaModuloAsync("/admin/platform/coda", [], $"/admin/platform/coda?handler=Ritenta&id={id}");

        Assert.True(SessioneProva.EReindirizzamento(riprova));
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var messaggio = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.Equal(StatoMessaggioOutbox.Pendente, messaggio.Stato);
        Assert.Equal(0, messaggio.Tentativi);
    }

    [Fact]
    public async Task UnTenantAdminEUnAnonimoNonVedonoLaCoda()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await InserisciFallitoAsync(scenario, "Errore");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var elenco = await anna.GetAsync("/admin/platform/coda");
        using var scarta = await anna.InviaModuloAsync("/admin", [], $"/admin/platform/coda?handler=Scarta&id={id}");
        using var anonimo = await scenario.SessioneA().GetAsync("/admin/platform/coda");

        Assert.Equal(HttpStatusCode.Forbidden, elenco.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, scarta.StatusCode);
        Assert.True(SessioneProva.EReindirizzamento(anonimo));
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(1, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(Ct));
    }

    private static async Task<Guid> InserisciFallitoAsync(ScenarioWeb scenario, string errore)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var messaggio = new MessaggioOutbox
        {
            Id = Guid.NewGuid(),
            IdTenant = scenario.A.IdTenant,
            Tipo = "InvioEmail",
            Payload = "PAYLOAD-RISERVATO",
            Stato = StatoMessaggioOutbox.Fallito,
            Tentativi = 8,
            UltimoErrore = errore,
            CreatoIlUtc = DateTime.UtcNow,
            ProssimoTentativoIlUtc = DateTime.UtcNow,
        };
        contesto.MessaggiOutbox.Add(messaggio);
        await contesto.SaveChangesAsync(Ct);
        return messaggio.Id;
    }
}

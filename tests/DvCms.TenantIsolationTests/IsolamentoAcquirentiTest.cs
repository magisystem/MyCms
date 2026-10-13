using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DvCms.Contracts.Piattaforma;
using DvCms.Data;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sugli acquirenti (F-000): account, conferme e sessioni non attraversano i tenant.</summary>
public partial class IsolamentoAcquirentiTest
{
    private const string Scelta = "una-password-scelta-da-me-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LaStessaEmailSiRegistraSuDueTenantComeAccountDistintiEUnLinkVaSoloSulSuoHost()
    {
        await using var web = await ScenarioWeb.CreaAsync(
            impostazioni: new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        await using var server = new ServerSmtpFinto();
        await DatiProva.ConfiguraSmtpAsync(web.Fabbrica.Services, web.A.IdTenant, server.Porta, Ct);
        await DatiProva.ConfiguraSmtpAsync(web.Fabbrica.Services, web.B.IdTenant, server.Porta, Ct);
        var platform = await web.CreaPlatformAdminAsync("root@esempio.it");
        foreach (var tenant in new[] { web.A, web.B })
        {
            await using var ambito = web.Fabbrica.Services.CreateAsyncScope();
            var piattaforma = ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>();
            Assert.True((await piattaforma.ImpostaCommercioAsync(platform, tenant.IdTenant, tenant.IdSito, true, Ct)).Riuscito);
            Assert.True((await piattaforma.ImpostaRegistrazioneAcquirentiAsync(platform, tenant.IdTenant, tenant.IdSito, true, Ct)).Riuscito);
        }

        using var inA = await web.SessioneA().InviaModuloAsync("/account/registrazione", [new("Email", "stessa@esempio.it"), new("Nome", "Di A")]);
        using var inB = await web.SessioneB().InviaModuloAsync("/account/registrazione", [new("Email", "stessa@esempio.it"), new("Nome", "Di B")]);
        while (await web.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct) > 0)
        {
        }

        var testoA = server.Ricevute.Single(e => e.Testo.Contains(web.A.Host, StringComparison.Ordinal)).Testo;
        var linkA = new Uri(LinkNelTesto().Match(testoA).Groups[1].Value);
        var tokenA = Regex.Match(linkA.Query, "token=([A-Za-z0-9_-]+)").Groups[1].Value;
        await using (var ambito = web.Fabbrica.Services.CreateAsyncScope())
        {
            var utenze = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.EmailNormalizzata == "stessa@esempio.it").ToListAsync(Ct);
            Assert.Equal(2, utenze.Count);
            Assert.Equal([web.A.IdTenant, web.B.IdTenant], utenze.Select(u => u.IdTenant!.Value).Order());
            Assert.All(utenze, u => Assert.Equal(StatoUtenza.InAttesaDiConferma, u.Stato));
        }

        // Il link di A sull'host di B non conferma nulla (nemmeno con una password valida).
        using var daB = await web.SessioneB().InviaModuloAsync("/account/accesso", [new("Token", tokenA), new("Password", Scelta), new("ConfermaPassword", Scelta)], "/account/conferma");
        Assert.Contains("non &#xE8; valido", await daB.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await using (var ambito = web.Fabbrica.Services.CreateAsyncScope())
        {
            var db = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            Assert.Equal(2, await db.Utenze.IgnoreQueryFilters().CountAsync(u => u.EmailNormalizzata == "stessa@esempio.it" && u.Stato == StatoUtenza.InAttesaDiConferma, Ct));
        }

        // Sull'host giusto conferma solo l'account di A; l'account di B resta in attesa e la password di A non vale su B.
        using var daA = await web.SessioneA().InviaModuloAsync("/account/accesso", [new("Token", tokenA), new("Password", Scelta), new("ConfermaPassword", Scelta)], "/account/conferma");
        Assert.Contains("Account attivato", await daA.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var sessioneA = web.SessioneA();
        using var accessoA = await sessioneA.InviaModuloAsync("/account/accesso", [new("Email", "stessa@esempio.it"), new("Password", Scelta)]);
        var sessioneB = web.SessioneB();
        using var accessoB = await sessioneB.InviaModuloAsync("/account/accesso", [new("Email", "stessa@esempio.it"), new("Password", Scelta)]);
        Assert.True(SessioneProva.EReindirizzamento(accessoA));
        Assert.False(SessioneProva.EReindirizzamento(accessoB));
        Assert.Null(sessioneB.CookieCliente);
    }

    [GeneratedRegex("(https?://[^\\s]+/account/conferma\\?token=[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkNelTesto();
}

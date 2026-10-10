using DvCms.Application.Accessi;
using DvCms.IntegrationTests.Supporto;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Il cookie di sfida del secondo fattore (ADR-0019): scade, è legato al tenant e non si falsifica.</summary>
public sealed class SfidaSecondoFattoreWebTest
{
    private static readonly Guid _tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly SfidaSecondoFattore _sfida = new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void LaSfidaEmessaSiLeggeEFinoAllaScadenza()
    {
        var orologio = new OrologioProva();
        var (servizio, cookie) = Emetti(orologio, destinazione: "/admin/media");

        Assert.True(servizio.TentaLeggi(ContestoCon(cookie), _tenant, out var letta));
        Assert.Equal(_sfida.IdUtenza, letta.IdUtenza);
        Assert.Equal(_sfida.TimbroSicurezza, letta.TimbroSicurezza);
        Assert.Equal("/admin/media", letta.Destinazione);

        orologio.Avanza(SfidaSecondoFattoreWeb.Validita - TimeSpan.FromSeconds(1));
        Assert.True(servizio.TentaLeggi(ContestoCon(cookie), _tenant, out _));
        orologio.Avanza(TimeSpan.FromSeconds(2));
        Assert.False(servizio.TentaLeggi(ContestoCon(cookie), _tenant, out _));
    }

    [Fact]
    public void LaSfidaNonValeSuUnAltroTenantNeManomessaNeDiAltraApplicazione()
    {
        var orologio = new OrologioProva();
        var (servizio, cookie) = Emetti(orologio);

        Assert.False(servizio.TentaLeggi(ContestoCon(cookie), Guid.NewGuid(), out _));
        Assert.False(servizio.TentaLeggi(ContestoCon(cookie[..^2] + "xx"), _tenant, out _));
        Assert.False(servizio.TentaLeggi(ContestoCon("non-un-cookie"), _tenant, out _));
        Assert.False(servizio.TentaLeggi(ContestoCon(new string('A', 5000)), _tenant, out _));
        Assert.False(servizio.TentaLeggi(new DefaultHttpContext(), _tenant, out _));

        // Cifrata con un altro anello di chiavi (altra applicazione o altro scopo): non si decifra.
        var altro = new SfidaSecondoFattoreWeb(new EphemeralDataProtectionProvider(), orologio, new AmbienteProva());
        Assert.False(altro.TentaLeggi(ContestoCon(cookie), _tenant, out _));
    }

    [Fact]
    public void IlCookieHaGliAttributiDiSicurezzaAttesiEVieneRimosso()
    {
        var orologio = new OrologioProva();
        var servizio = Crea(orologio, new EphemeralDataProtectionProvider());
        var contesto = new DefaultHttpContext();

        servizio.Emetti(contesto, _sfida, _tenant, null);
        var emesso = contesto.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", emesso, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", emesso, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/admin", emesso, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=300", emesso, StringComparison.OrdinalIgnoreCase);

        var rimozione = new DefaultHttpContext();
        servizio.Rimuovi(rimozione);
        Assert.Contains(SfidaSecondoFattoreWeb.NomeCookie + "=;", rimozione.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    private static (SfidaSecondoFattoreWeb Servizio, string Cookie) Emetti(OrologioProva orologio, string? destinazione = null)
    {
        var servizio = Crea(orologio, new EphemeralDataProtectionProvider());
        var contesto = new DefaultHttpContext();
        servizio.Emetti(contesto, _sfida, _tenant, destinazione);
        var intestazione = contesto.Response.Headers.SetCookie.ToString();
        var valore = intestazione[(SfidaSecondoFattoreWeb.NomeCookie.Length + 1)..intestazione.IndexOf(';', StringComparison.Ordinal)];
        return (servizio, Uri.UnescapeDataString(valore));
    }

    private static SfidaSecondoFattoreWeb Crea(OrologioProva orologio, IDataProtectionProvider provider) =>
        new(provider, orologio, new AmbienteProva());

    private static DefaultHttpContext ContestoCon(string cookie)
    {
        var contesto = new DefaultHttpContext();
        contesto.Request.Headers.Cookie = $"{SfidaSecondoFattoreWeb.NomeCookie}={Uri.EscapeDataString(cookie)}";
        return contesto;
    }

    private sealed class AmbienteProva : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Prova";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}

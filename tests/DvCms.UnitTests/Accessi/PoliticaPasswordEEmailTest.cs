using DvCms.Core.Accessi;
using DvCms.DTO.Accessi;
using DvCms.Extender;
using DvCms.Models.Accessi;
using Xunit;

namespace DvCms.UnitTests.Accessi;

public class PoliticaPasswordEEmailTest
{
    [Theory]
    [InlineData(null, ErrorePassword.Vuota)]
    [InlineData("", ErrorePassword.Vuota)]
    [InlineData("corta", ErrorePassword.TroppoCorta)]
    [InlineData("undici-char", ErrorePassword.TroppoCorta)]
    public void PasswordNonValide(string? password, ErrorePassword atteso)
    {
        Assert.Equal(atteso, PoliticaPassword.Valida(password));
    }

    [Fact]
    public void PasswordValideEntroLimiti()
    {
        Assert.Null(PoliticaPassword.Valida(new string('x', PoliticaPassword.LunghezzaMinima)));
        Assert.Null(PoliticaPassword.Valida(new string('x', PoliticaPassword.LunghezzaMassima)));
        Assert.Equal(ErrorePassword.TroppoLunga, PoliticaPassword.Valida(new string('x', PoliticaPassword.LunghezzaMassima + 1)));
    }

    [Theory]
    [InlineData("Anna.Rossi@Esempio.IT", "anna.rossi@esempio.it")]
    [InlineData("  anna@esempio.it  ", "anna@esempio.it")]
    [InlineData("a+b@sotto.esempio.it", "a+b@sotto.esempio.it")]
    [InlineData("admin@localhost", "admin@localhost")]
    public void EmailValideNormalizzate(string ingresso, string atteso)
    {
        Assert.True(NormalizzatoreEmail.TentaNormalizza(ingresso, out var normalizzata));
        Assert.Equal(atteso, normalizzata);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("senza-chiocciola")]
    [InlineData("due@@esempio.it")]
    [InlineData("a@b@esempio.it")]
    [InlineData("Anna <anna@esempio.it>")]
    [InlineData("anna@esempio.it, bruno@esempio.it")]
    [InlineData("anna@senzapunto")]
    [InlineData("spazio nel@esempio.it")]
    [InlineData("anna@esempio.it\r\nBcc: x@y.it")]
    public void EmailNonValide(string? ingresso)
    {
        Assert.False(NormalizzatoreEmail.TentaNormalizza(ingresso, out var normalizzata));
        Assert.Equal(string.Empty, normalizzata);
    }

    [Fact]
    public void UtenzaRispettaLeRegoleDiRuoloETenant()
    {
        var orologio = TimeProvider.System;
        var tenant = Guid.NewGuid();

        var platform = Utenza.Crea(RuoloUtenza.PlatformAdmin, null, "p@esempio.it", "P", "hash", false, orologio);
        var admin = Utenza.Crea(RuoloUtenza.TenantAdmin, tenant, "A@Esempio.it", "A", "hash", true, orologio);

        Assert.Null(platform.IdTenant);
        Assert.Equal(tenant, admin.IdTenant);
        Assert.Equal("a@esempio.it", admin.EmailNormalizzata);
        Assert.Equal("A@Esempio.it", admin.Email);
        Assert.True(admin.DeveCambiarePassword);
        Assert.NotEqual(Guid.Empty, admin.TimbroSicurezza);
        Assert.Throws<ArgumentException>(() => Utenza.Crea(RuoloUtenza.PlatformAdmin, tenant, "x@esempio.it", "X", "hash", false, orologio));
        Assert.Throws<ArgumentException>(() => Utenza.Crea(RuoloUtenza.TenantAdmin, null, "x@esempio.it", "X", "hash", false, orologio));
        Assert.Throws<ArgumentException>(() => Utenza.Crea(RuoloUtenza.Utente, null, "x@esempio.it", "X", "hash", false, orologio));
        Assert.Throws<ArgumentException>(() => Utenza.Crea(RuoloUtenza.TenantAdmin, tenant, "non-una-email", "X", "hash", false, orologio));
    }
}

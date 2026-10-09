using DvCms.Application.Email;
using DvCms.Domain.Email;
using Xunit;

namespace DvCms.UnitTests.Dominio;

public class DefinizioniProviderEmailTest
{
    private static Dictionary<string, string> SmtpValido() => new()
    {
        ["Host"] = "smtp.esempio.it",
        ["Porta"] = "587",
        ["ModalitaTls"] = "StartTls",
        ["IndirizzoMittente"] = "noreply@esempio.it",
    };

    [Fact]
    public void UnaConfigurazioneSmtpCompletaEValida()
    {
        Assert.Empty(DefinizioniProviderEmail.Valida(ProviderEmail.Smtp, SmtpValido()));
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Porta")]
    [InlineData("ModalitaTls")]
    [InlineData("IndirizzoMittente")]
    public void UnCampoObbligatorioMancanteVieneSegnalato(string campo)
    {
        var valori = SmtpValido();
        valori.Remove(campo);

        Assert.Equal([campo], DefinizioniProviderEmail.Valida(ProviderEmail.Smtp, valori));
    }

    [Theory]
    [InlineData("Porta", "0")]
    [InlineData("Porta", "70000")]
    [InlineData("Porta", "abc")]
    [InlineData("Porta", "-1")]
    [InlineData("ModalitaTls", "Qualcosa")]
    [InlineData("IndirizzoMittente", "non-una-email")]
    [InlineData("Host", "host\r\nBcc: x")]
    public void ValoriNonValidiVengonoSegnalatiPerNomeDelCampo(string campo, string valore)
    {
        var valori = SmtpValido();
        valori[campo] = valore;

        Assert.Equal([campo], DefinizioniProviderEmail.Valida(ProviderEmail.Smtp, valori));
    }

    [Fact]
    public void CampiDiUnAltroProviderNonSonoAccettati()
    {
        var valori = SmtpValido();
        valori["ChiaveApi"] = "x";

        Assert.Equal(["ChiaveApi"], DefinizioniProviderEmail.Valida(ProviderEmail.Smtp, valori));
    }

    [Fact]
    public void UnProviderSconosciutoERifiutato()
    {
        Assert.Equal(["Provider"], DefinizioniProviderEmail.Valida((ProviderEmail)99, SmtpValido()));
    }

    [Fact]
    public void GliErroriNonContengonoIValori()
    {
        var valori = SmtpValido();
        valori["Porta"] = "segretissimo";

        Assert.DoesNotContain(DefinizioniProviderEmail.Valida(ProviderEmail.Smtp, valori), errore => errore.Contains("segretissimo", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProviderEmail.Smtp, "Password")]
    [InlineData(ProviderEmail.Graph, "SegretoClient")]
    [InlineData(ProviderEmail.Gmail, "CredenzialiJson")]
    [InlineData(ProviderEmail.SendGrid, "ChiaveApi")]
    public void IlCampoSegretoDiOgniProviderEMarcatoComeTale(ProviderEmail provider, string campo)
    {
        Assert.True(DefinizioniProviderEmail.Campi(provider).Single(c => c.Nome == campo).Segreto);
        Assert.All(DefinizioniProviderEmail.Campi(provider).Where(c => c.Nome != campo), c => Assert.False(c.Segreto));
    }

    [Fact]
    public void TuttiIProviderHannoCampiEIlCampoMittente()
    {
        foreach (var provider in Enum.GetValues<ProviderEmail>())
        {
            var campi = DefinizioniProviderEmail.Campi(provider);
            Assert.NotEmpty(campi);
            Assert.Contains(campi, c => c.Tipo == TipoCampoEmail.Email && c.Obbligatorio);
        }
    }

    [Fact]
    public void LaRappresentazioneDelleImpostazioniNonMostraIValori()
    {
        var impostazioni = new ImpostazioniEmail(ProviderEmail.Smtp, new Dictionary<string, string> { ["Password"] = "segretissimo" });

        Assert.DoesNotContain("segretissimo", impostazioni.ToString(), StringComparison.Ordinal);
    }
}

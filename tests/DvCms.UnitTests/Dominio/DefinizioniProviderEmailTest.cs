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

            // Con il consenso OAuth il mittente è la casella collegata, non un campo: l'app ha invece il segreto del client.
            if (provider.UsaOAuth())
            {
                Assert.Contains(campi, c => c.Nome == "SegretoClient" && c.Segreto && c.Obbligatorio);
            }
            else
            {
                Assert.Contains(campi, c => c.Tipo == TipoCampoEmail.Email && c.Obbligatorio);
            }
        }
    }

    [Fact]
    public void LaRappresentazioneDelleImpostazioniNonMostraIValori()
    {
        var impostazioni = new ImpostazioniEmail(ProviderEmail.Smtp, new Dictionary<string, string> { ["Password"] = "segretissimo" });

        Assert.DoesNotContain("segretissimo", impostazioni.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TenantMicrosoft", "contoso/../x")]
    [InlineData("TenantMicrosoft", "t?x=1")]
    [InlineData("TenantMicrosoft", "con spazio")]
    [InlineData("IdClient", "client#1")]
    [InlineData("IdClient", "a@b")]
    public void GliIdentificativiDiGraphNonAccettanoCaratteriDaIndirizzo(string campo, string valore)
    {
        var valori = new Dictionary<string, string>
        {
            ["TenantMicrosoft"] = "contoso.onmicrosoft.com",
            ["IdClient"] = "00000000-0000-0000-0000-000000000000",
            ["SegretoClient"] = "x",
            ["CasellaMittente"] = "noreply@esempio.it",
        };
        Assert.Empty(DefinizioniProviderEmail.Valida(ProviderEmail.Graph, valori));
        valori[campo] = valore;

        Assert.Equal([campo], DefinizioniProviderEmail.Valida(ProviderEmail.Graph, valori));
    }

    [Theory]
    [InlineData("non json")]
    [InlineData("[]")]
    [InlineData("{\"type\":\"authorized_user\",\"client_email\":\"a@b.it\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\"}")]
    [InlineData("{\"type\":\"service_account\",\"client_email\":\"non-email\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\"}")]
    [InlineData("{\"type\":\"service_account\",\"client_email\":\"a@b.it\"}")]
    [InlineData("{\"type\":\"service_account\",\"client_email\":\"a@b.it\",\"private_key\":\"niente\"}")]
    public void LeCredenzialiGoogleDevonEssereUnAccountDiServizio(string json)
    {
        var valori = new Dictionary<string, string> { ["UtenteMittente"] = "noreply@esempio.it", ["CredenzialiJson"] = json };

        Assert.Equal(["CredenzialiJson"], DefinizioniProviderEmail.Valida(ProviderEmail.Gmail, valori));
    }

    [Fact]
    public void LeCredenzialiGoogleValideSonoAccettate()
    {
        var valori = new Dictionary<string, string>
        {
            ["UtenteMittente"] = "noreply@esempio.it",
            ["CredenzialiJson"] = "{\"type\":\"service_account\",\"client_email\":\"a@b.iam.gserviceaccount.com\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\\nabc\\n-----END PRIVATE KEY-----\\n\"}",
        };

        Assert.Empty(DefinizioniProviderEmail.Valida(ProviderEmail.Gmail, valori));
    }
}

using DvCms.DTO.Accessi;
using DvCms.Infrastructure.Accessi;
using Xunit;

namespace DvCms.UnitTests.Accessi;

public class HashPasswordBCryptTest
{
    private static HashPasswordBCrypt Crea(int fattore = 4) =>
        new(new OpzioniSicurezza(fattore, 5, TimeSpan.FromMinutes(5)));

    [Fact]
    public void HashSiVerificaConLaPasswordGiusta()
    {
        var hash = Crea();
        var calcolato = hash.Calcola("una password lunga e corretta");

        Assert.True(hash.Verifica("una password lunga e corretta", calcolato));
        Assert.False(hash.Verifica("una password lunga e sbagliata", calcolato));
        Assert.StartsWith("$2", calcolato, StringComparison.Ordinal);
    }

    [Fact]
    public void LoStessoTestoDaHashDiversiPerViaDelSale()
    {
        var hash = Crea();

        Assert.NotEqual(hash.Calcola("stessa password 123"), hash.Calcola("stessa password 123"));
    }

    [Fact]
    public void PasswordLunghePiuDi72ByteSonoDistinte()
    {
        var hash = Crea();
        var prefisso = new string('a', 100);
        var calcolato = hash.Calcola(prefisso + "X");

        // Con BCrypt semplice il testo oltre il byte 72 verrebbe ignorato: qui no (pre-hash SHA-384).
        Assert.True(hash.Verifica(prefisso + "X", calcolato));
        Assert.False(hash.Verifica(prefisso + "Y", calcolato));
    }

    [Theory]
    [InlineData("")]
    [InlineData("non-un-hash")]
    [InlineData("$2a$04$troppo-corto")]
    public void HashNonValidoNonVerificaENonSollevaEccezioni(string hashNonValido)
    {
        var hash = Crea();

        Assert.False(hash.Verifica("qualsiasi password", hashNonValido));
        Assert.False(hash.Verifica(string.Empty, hash.Calcola("password valida 123")));
    }

    [Fact]
    public void HashConCostoInferioreRichiedeAggiornamento()
    {
        var vecchio = Crea(4).Calcola("password di prova 123");

        Assert.True(Crea(5).RichiedeAggiornamento(vecchio));
        Assert.False(Crea(4).RichiedeAggiornamento(vecchio));
        Assert.True(Crea(4).RichiedeAggiornamento("non-un-hash"));
    }
}

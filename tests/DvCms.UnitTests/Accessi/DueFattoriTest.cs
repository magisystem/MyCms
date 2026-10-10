using System.Text;
using DvCms.Application.Accessi.DueFattori;
using Xunit;

namespace DvCms.UnitTests.Accessi;

public sealed class DueFattoriTest
{
    private static readonly byte[] _segretoRfc = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void IlCodiceCoincideConIVettoriDiProvaDellaRFC6238(long secondi, string atteso)
    {
        var passo = Totp.PassoDi(DateTimeOffset.FromUnixTimeSeconds(secondi));

        Assert.Equal(atteso, Totp.CalcolaCodice(_segretoRfc, passo));
    }

    [Fact]
    public void LaVerificaAccettaIlPassoCorrenteEIViciniMaNonGliAltri()
    {
        var adesso = DateTimeOffset.FromUnixTimeSeconds(1111111111L);
        var passo = Totp.PassoDi(adesso);

        Assert.Equal(passo, Totp.Verifica(_segretoRfc, Totp.CalcolaCodice(_segretoRfc, passo), adesso));
        Assert.Equal(passo - 1, Totp.Verifica(_segretoRfc, Totp.CalcolaCodice(_segretoRfc, passo - 1), adesso));
        Assert.Equal(passo + 1, Totp.Verifica(_segretoRfc, Totp.CalcolaCodice(_segretoRfc, passo + 1), adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, Totp.CalcolaCodice(_segretoRfc, passo - 2), adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, Totp.CalcolaCodice(_segretoRfc, passo + 2), adesso));
    }

    [Fact]
    public void LaVerificaAccettaGliSpaziERifiutaLeFormeNonValide()
    {
        var adesso = DateTimeOffset.FromUnixTimeSeconds(1111111111L);
        var codice = Totp.CalcolaCodice(_segretoRfc, Totp.PassoDi(adesso));

        Assert.NotNull(Totp.Verifica(_segretoRfc, codice.Insert(3, " "), adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, null, adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, string.Empty, adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, codice[..5], adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, codice + "0", adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, "abcdef", adesso));
        Assert.Null(Totp.Verifica(_segretoRfc, "١٢٣٤٥٦", adesso));
    }

    [Fact]
    public void IlSegretoGeneratoHaLaLunghezzaAttesaEdEDiversoAOgniChiamata()
    {
        var primo = Totp.GeneraSegreto();
        var secondo = Totp.GeneraSegreto();

        Assert.Equal(Totp.LunghezzaSegretoByte, primo.Length);
        Assert.NotEqual(primo, secondo);
    }

    [Fact]
    public void LUriContieneSegretoEmittenteEParametriConICaratteriSpecialiCodificati()
    {
        var uri = Totp.CreaUri(_segretoRfc, "DvCms", "mario rossi@esempio.it");

        Assert.StartsWith("otpauth://totp/DvCms:mario%20rossi%40esempio.it?", uri, StringComparison.Ordinal);
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=DvCms", uri, StringComparison.Ordinal);
        Assert.Contains("digits=6&period=30", uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32CoincideConIVettoriDellaRFC4648(string testo, string atteso)
    {
        var dati = Encoding.ASCII.GetBytes(testo);

        Assert.Equal(atteso, Base32.Codifica(dati));
        Assert.True(Base32.TentaDecodifica(atteso.ToLowerInvariant(), out var decodificato));
        Assert.Equal(dati, decodificato);
    }

    [Fact]
    public void Base32RifiutaCaratteriFuoriAlfabetoEIgnoraSpaziETrattini()
    {
        Assert.False(Base32.TentaDecodifica("MZXW6!", out _));
        Assert.False(Base32.TentaDecodifica("MZXW1", out _));
        Assert.False(Base32.TentaDecodifica(null, out _));
        Assert.True(Base32.TentaDecodifica("MZXW-6YTB OI", out var dati));
        Assert.Equal("foobar", Encoding.ASCII.GetString(dati));
    }

    [Fact]
    public void ICodiciDiRecuperoSonoDieciDistintiNelFormatoAtteso()
    {
        var codici = CodiciRecupero.Genera();

        Assert.Equal(CodiciRecupero.Quantita, codici.Count);
        Assert.Equal(codici.Count, codici.Distinct().Count());
        Assert.All(codici, codice =>
        {
            Assert.Matches("^[A-Z2-7]{4}(-[A-Z2-7]{4}){3}$", codice);
            Assert.NotNull(CodiciRecupero.Normalizza(codice));
        });
    }

    [Fact]
    public void LaNormalizzazioneDeiCodiciDiRecuperoIgnoraFormaERifiutaIlResto()
    {
        Assert.Equal("ABCDEFGH23456723", CodiciRecupero.Normalizza("abcd-efgh 2345-6723"));
        Assert.Null(CodiciRecupero.Normalizza("ABCD-EFGH-2345"));
        Assert.Null(CodiciRecupero.Normalizza("ABCD-EFGH-2345-6781"));
        Assert.Null(CodiciRecupero.Normalizza(null));
        Assert.Null(CodiciRecupero.Normalizza("  "));
    }

    [Fact]
    public void LHashDelCodiceEStabileEDipendeDallaFormaCanonica()
    {
        var a = CodiciRecupero.Hash("ABCDEFGH23456723");

        Assert.Equal(32, a.Length);
        Assert.Equal(a, CodiciRecupero.Hash("ABCDEFGH23456723"));
        Assert.NotEqual(a, CodiciRecupero.Hash("ABCDEFGH23456724"));
    }
}

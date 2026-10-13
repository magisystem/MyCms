using DvCms.Application.Accessi;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Impostazioni dei pagamenti manuali (ADR-0023): solo l'amministratore del tenant, validazione, concorrenza, audit e isolamento.</summary>
public class ImpostazioniPagamentiOfflineTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ImpostazioniPagamentoOffline _valide = ContestoNegozio.ImpostazioniOffline;

    [Fact]
    public async Task IlTenantAdminSalvaELeggeLeImpostazioniConIbanNormalizzatoEAudit()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var vuote = await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        Assert.Equal(ImpostazioniPagamentoOffline.Spente, vuote.Impostazioni);

        var esito = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminA, _valide with { Iban = "it60 x054 2811 1010 0000 0123 456", Bic = "bcit itmm", Intestatario = "  Negozio Srl  " }, vuote.TokenConcorrenza, Ct));

        Assert.True(esito.Riuscito, string.Join(',', esito.CampiNonValidi ?? []));
        var letta = (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Impostazioni;
        Assert.Equal((ContestoNegozio.IbanProva, "BCITITMM", "Negozio Srl"), (letta.Iban, letta.Bic, letta.Intestatario));
        Assert.Equal(_valide with { Iban = ContestoNegozio.IbanProva, Bic = "BCITITMM" }, letta);
        var audit = Assert.Single(await c.AuditAsync(AzioniAudit.ModificaPagamentiOffline));
        Assert.Equal(c.AdminA, audit.IdUtenzaAttore);
        Assert.DoesNotContain("IT60", audit.Dettagli ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ILimitiFacoltativiSiPossonoToglierePassandoNullo()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.ConfiguraOfflineAsync(c.A, _valide with { CommissioneGratisSopra = 100m, OrdineMinimo = 10m, OrdineMassimo = 500m });
        await c.ConfiguraOfflineAsync(c.A, _valide);

        var letta = (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Impostazioni;

        Assert.Equal((null, null, null), (letta.CommissioneGratisSopra, letta.OrdineMinimo, letta.OrdineMassimo));
    }

    public static TheoryData<string, ImpostazioniPagamentoOffline> Sbagliate => new()
    {
        { "Iban", _valide with { Iban = null } },
        { "Iban", _valide with { Iban = "IT60X0542811101000000123457" } },
        { "Intestatario", _valide with { Intestatario = " " } },
        { "Intestatario", _valide with { Intestatario = "Mario\u0007Rossi" } },
        { "Bic", _valide with { Bic = "ABC123" } },
        { "GiorniPerPagare", _valide with { GiorniPerPagare = 0 } },
        { "GiorniPerPagare", _valide with { GiorniPerPagare = 61 } },
        { "CommissioneFissa", _valide with { CommissioneFissa = -0.01m } },
        { "CommissioneFissa", _valide with { CommissioneFissa = 4.005m } },
        { "CommissioneFissa", _valide with { CommissioneFissa = 100_000.01m } },
        { "CommissionePercentuale", _valide with { CommissionePercentuale = 100.01m } },
        { "CommissionePercentuale", _valide with { CommissionePercentuale = 1.234m } },
        { "CommissioneGratisSopra", _valide with { CommissioneGratisSopra = -1m } },
        { "OrdineMinimo", _valide with { OrdineMinimo = -5m } },
        { "OrdineMassimo", _valide with { OrdineMinimo = 100m, OrdineMassimo = 50m } },
        { "IstruzioniBonifico", _valide with { IstruzioniBonifico = new string('x', 501) } },
        { "IstruzioniContrassegno", _valide with { IstruzioniContrassegno = "a\u0000b" } },
    };

    [Theory]
    [MemberData(nameof(Sbagliate))]
    public async Task DatiSbagliatiSiRifiutanoIndicandoIlCampoESenzaSalvareNulla(string campo, ImpostazioniPagamentoOffline dati)
    {
        await using var c = await ContestoNegozio.CreaAsync();

        var esito = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminA, dati, null, Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Contains(campo, esito.CampiNonValidi!);
        Assert.Equal(ImpostazioniPagamentoOffline.Spente, (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Impostazioni);
        Assert.Empty(await c.AuditAsync(AzioniAudit.ModificaPagamentiOffline));
    }

    [Fact]
    public async Task IlBonificoSpentoNonRichiedeIbanMaUnoSbagliatoSiRifiutaComunque()
    {
        await using var c = await ContestoNegozio.CreaAsync();

        var senza = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminA, ImpostazioniPagamentoOffline.Spente with { ContrassegnoAttivo = true, CommissioneFissa = 3m }, null, Ct));
        var sbagliato = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminA, ImpostazioniPagamentoOffline.Spente with { Iban = "IT00" }, null, Ct));

        Assert.True(senza.Riuscito);
        Assert.Equal(CodiceEsito.DatiNonValidi, sbagliato.Codice);
    }

    [Fact]
    public async Task SoloGliAmministratoriDelTenantPossonoLeggereESalvare()
    {
        await using var c = await ContestoNegozio.CreaAsync();

        var cliente = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.Cliente, _valide, null, Ct));
        var altroTenant = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminB, _valide, null, Ct));
        var platform = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.Platform, _valide, null, Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, cliente.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, altroTenant.Codice);
        Assert.True(platform.Riuscito);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.Cliente, Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminB, Ct)));
        Assert.Equal(2, (await c.AuditAsync(AzioniAudit.OperazioneNegata)).Count);
    }

    [Fact]
    public async Task SenzaNegozioAttivoNonSiSalva()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.SpegniNegozioAsync(c.A);

        var esito = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminA, _valide, null, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, esito.Codice);
    }

    [Fact]
    public async Task UnaModificaConcorrenteDaConflittoEUnTokenIllegibileEDatiNonValidi()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.ConfiguraOfflineAsync(c.A);
        var vecchio = (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).TokenConcorrenza;
        await c.ConfiguraOfflineAsync(c.A, _valide with { CommissioneFissa = 5m });

        var obsoleta = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminA, _valide with { CommissioneFissa = 9m }, vecchio, Ct));
        var illeggibile = await c.ComeOfflineAsync(c.A, s => s.SalvaAsync(c.AdminA, _valide, "non-base64!", Ct));

        Assert.Equal(CodiceEsito.Conflitto, obsoleta.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, illeggibile.Codice);
        Assert.Equal(5m, (await c.ComeOfflineAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Impostazioni.CommissioneFissa);
    }

    [Fact]
    public async Task IlClienteVedeSoloCioCheEAttivoEOgniTenantHaLeSue()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.ConfiguraOfflineAsync(c.A, _valide with { ContrassegnoAttivo = false });
        await c.ConfiguraOfflineAsync(c.B, ContestoNegozio.ImpostazioniOffline with { BonificoAttivo = false, Iban = "DE89370400440532013000", CommissioneFissa = 7m });

        var cliente = await c.ComeOfflineAsync(c.A, s => s.LeggiPerClienteAsync(c.Cliente, Ct));
        var clienteB = await c.ComeOfflineAsync(c.B, s => s.LeggiPerClienteAsync(c.ClienteDiB, Ct));

        // A: bonifico attivo (con coordinate), contrassegno spento (nessun dettaglio).
        Assert.True(cliente.BonificoAttivo);
        Assert.Equal((ContestoNegozio.IbanProva, false, 0m, null), (cliente.Iban, cliente.ContrassegnoAttivo, cliente.CommissioneFissa, cliente.IstruzioniContrassegno));
        // B: contrassegno attivo, bonifico spento (l'IBAN salvato non si vede).
        Assert.False(clienteB.BonificoAttivo);
        Assert.Null(clienteB.Iban);
        Assert.Equal((true, 7m), (clienteB.ContrassegnoAttivo, clienteB.CommissioneFissa));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeOfflineAsync(c.A, s => s.LeggiPerClienteAsync(c.ClienteDiB, Ct)));
    }

    [Fact]
    public async Task SenzaNegozioAttivoIlClienteNonVedeNulla()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.ConfiguraOfflineAsync(c.A);
        await c.SpegniNegozioAsync(c.A);

        Assert.Equal(ImpostazioniPagamentoOffline.Spente, await c.ComeOfflineAsync(c.A, s => s.LeggiPerClienteAsync(c.Cliente, Ct)));
    }
}

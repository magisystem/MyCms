using DvCms.Application.Accessi;
using DvCms.Application.Cms;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Cms;
using DvCms.Domain.Localizzazione;
using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Cms;

/// <summary>Gestione delle pagine nell'admin (C-002) su SQL Server reale.</summary>
public class ServizioPagineTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreaUnaBozzaConSlugDalTitoloELinguaPredefinita()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina(null, "Chi siamo", "Testo", null, false), Ct));

        Assert.True(esito.Riuscito);
        var dettaglio = await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, esito.Id!.Value, Ct));
        Assert.Equal("chi-siamo", dettaglio!.Slug);
        Assert.Equal("it", dettaglio.CodiceLingua);
        Assert.Equal(StatoPagina.Bozza, dettaglio.Stato);
        Assert.Equal("Testo", dettaglio.Contenuto);
        Assert.Single(dettaglio.Revisioni);
        Assert.Equal("anna@esempio.it", dettaglio.Revisioni[0].Autore);
    }

    [Theory]
    [InlineData("", "Testo", "pagina", "it")]
    [InlineData("Titolo", "Testo", "admin", "it")]
    [InlineData("Titolo", "Testo", "lib", "it")]
    [InlineData("Titolo", "Testo", "con spazio", "it")]
    [InlineData("Titolo", "Testo", "file.html", "it")]
    [InlineData("Titolo", "Testo", "pagina", "xx")]
    [InlineData("!!!", "Testo", "", "it")]
    public async Task LaCreazioneRifiutaDatiNonValidi(string titolo, string contenuto, string slug, string lingua)
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina(slug, titolo, contenuto, lingua, false), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Empty(await s.ComeAsync(s.A, servizio => servizio.ElencaAsync(s.AdminA, Ct)));
    }

    [Fact]
    public async Task LoSlugEUnicoPerSitoELinguaMaPuoRipetersiAltrove()
    {
        await using var s = await Scenario.CreaAsync();
        await s.CreaPaginaAsync(s.A, s.AdminA, "contatti");

        var stessaLingua = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("contatti", "Altro", "x", "it", false), Ct));
        var altraLingua = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("contatti", "Contacts", "x", "en", false), Ct));
        var altroTenant = await s.ComeAsync(s.B, servizio => servizio.CreaAsync(s.AdminB, new DatiNuovaPagina("contatti", "Contatti B", "x", "it", false), Ct));

        Assert.Equal(CodiceEsito.GiaPresente, stessaLingua.Codice);
        Assert.True(altraLingua.Riuscito);
        Assert.True(altroTenant.Riuscito);
    }

    [Fact]
    public async Task LoSlugPuoRipetersiSuUnAltroSitoDelloStessoTenant()
    {
        await using var s = await Scenario.CreaAsync();
        await s.CreaPaginaAsync(s.A, s.AdminA, "contatti");
        var secondoSito = await s.AggiungiSitoAsync(s.A);

        var esito = await s.ComeAsync(secondoSito, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("contatti", "Contatti 2", "x", "it", false), Ct));

        Assert.True(esito.Riuscito);
        Assert.Single(await s.ComeAsync(secondoSito, servizio => servizio.ElencaAsync(s.AdminA, Ct)));
        Assert.Single(await s.ComeAsync(s.A, servizio => servizio.ElencaAsync(s.AdminA, Ct)));
    }

    [Fact]
    public async Task UnaSolaPaginaInizialePerLingua()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("home", "Home", "x", "it", true), Ct));

        var seconda = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("benvenuto", "Benvenuto", "x", "it", true), Ct));
        var inglese = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("home-en", "Home", "x", "en", true), Ct));

        Assert.Equal(CodiceEsito.PaginaInizialeEsistente, seconda.Codice);
        Assert.True(inglese.Riuscito);
    }

    [Fact]
    public async Task SalvareCreaUnaNuovaRevisioneEIlTokenVecchioDaConflitto()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;

        var primo = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, "Titolo 2", "Testo 2", "ritocco", false, letta.TokenConcorrenza), Ct));
        var concorrente = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, "Titolo 3", "Testo 3", null, false, letta.TokenConcorrenza), Ct));

        Assert.True(primo.Riuscito);
        Assert.Equal(CodiceEsito.Conflitto, concorrente.Codice);
        var dopo = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal("Titolo 2", dopo.Titolo);
        Assert.Equal(2, dopo.Revisioni.Count);
        Assert.Equal("ritocco", dopo.Revisioni[0].Nota);
        Assert.NotEqual(letta.TokenConcorrenza, dopo.TokenConcorrenza);
    }

    [Fact]
    public async Task SalvareSenzaModificheNonCreaRevisioni()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;

        var esito = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, letta.Titolo, letta.Contenuto, null, false, letta.TokenConcorrenza), Ct));

        Assert.True(esito.Riuscito);
        Assert.Single((await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!.Revisioni);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("non-base64!!")]
    public async Task SenzaTokenValidoLaModificaEDatiNonValidi(string? token)
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");

        var esito = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, "T", "C", null, false, token), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
    }

    [Fact]
    public async Task LoSlugSiCambiaSoloSeLibero()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "vecchio");
        await s.CreaPaginaAsync(s.A, s.AdminA, "occupato");
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;

        var occupato = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina("occupato", letta.Titolo, letta.Contenuto, null, false, letta.TokenConcorrenza), Ct));
        var riservato = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina("admin", letta.Titolo, letta.Contenuto, null, false, letta.TokenConcorrenza), Ct));
        var nuovo = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina("Nuovo", letta.Titolo, letta.Contenuto, null, false, letta.TokenConcorrenza), Ct));
        letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        await s.ComeAsync(s.A, servizio => servizio.PubblicaAsync(s.AdminA, id, letta.TokenConcorrenza, Ct));
        letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        var dopoPubblicazione = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina("altro", letta.Titolo, letta.Contenuto, null, false, letta.TokenConcorrenza), Ct));

        Assert.Equal(CodiceEsito.GiaPresente, occupato.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, riservato.Codice);
        Assert.True(nuovo.Riuscito);
        letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.True(dopoPubblicazione.Riuscito);
        Assert.Equal("altro", letta.Slug);
    }

    [Fact]
    public async Task CambiareSlugDiUnaPaginaPubblicataLasciaUnReindirizzamento()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "vecchio");
        await s.PubblicaAsync(s.A, id);

        await s.RinominaAsync(s.A, id, "nuovo");

        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal("nuovo", letta.Slug);
        Assert.Equal(["vecchio"], letta.Reindirizzamenti!.Select(r => r.SlugOrigine));
        Assert.Equal(1, await s.ContaOutboxAsync(TipiEventoPagina.Rinominata));
    }

    [Fact]
    public async Task UnaPaginaMaiPubblicataCheCambiaSlugNonLasciaReindirizzamenti()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "bozza");

        await s.RinominaAsync(s.A, id, "bozza-nuova");

        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Empty(letta.Reindirizzamenti!);
        Assert.Equal(0, await s.ContaOutboxAsync(TipiEventoPagina.Rinominata));
    }

    [Fact]
    public async Task PiuCambiDiSlugNonFormanoCatene()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "a");
        await s.PubblicaAsync(s.A, id);

        await s.RinominaAsync(s.A, id, "b");
        await s.RinominaAsync(s.A, id, "c");

        // Entrambi i vecchi indirizzi puntano alla pagina (quindi allo slug attuale), nessuno punta a un altro reindirizzamento.
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(["a", "b"], letta.Reindirizzamenti!.Select(r => r.SlugOrigine));
        Assert.Equal(1, await s.ContaRedirectAsync(r => r.IdPagina == id && r.SlugOrigine == "a"));
        Assert.Equal(0, await s.ContaRedirectAsync(r => r.SlugOrigine == "c"));
    }

    [Fact]
    public async Task TornareAlloSlugOriginaleTogliIlReindirizzamentoCheSarebbeUnCiclo()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "a");
        await s.PubblicaAsync(s.A, id);
        await s.RinominaAsync(s.A, id, "b");

        await s.RinominaAsync(s.A, id, "a");

        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal("a", letta.Slug);
        Assert.Equal(["b"], letta.Reindirizzamenti!.Select(r => r.SlugOrigine));
    }

    [Fact]
    public async Task UnaPaginaNuovaCheUsaUnVecchioSlugPrendeIlPostoDelReindirizzamento()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "vecchio");
        await s.PubblicaAsync(s.A, id);
        await s.RinominaAsync(s.A, id, "nuovo");

        var altra = await s.CreaPaginaAsync(s.A, s.AdminA, "vecchio");

        Assert.NotEqual(id, altra);
        Assert.Equal(0, await s.ContaRedirectAsync(r => r.SlugOrigine == "vecchio"));
    }

    [Fact]
    public async Task UnaPaginaCheRinominaOccupandoUnVecchioSlugDiUnAltraPrendeIlPosto()
    {
        await using var s = await Scenario.CreaAsync();
        var prima = await s.CreaPaginaAsync(s.A, s.AdminA, "vecchio");
        await s.PubblicaAsync(s.A, prima);
        await s.RinominaAsync(s.A, prima, "nuovo");
        var seconda = await s.CreaPaginaAsync(s.A, s.AdminA, "altra");

        await s.RinominaAsync(s.A, seconda, "vecchio");

        Assert.Equal(0, await s.ContaRedirectAsync(r => r.IdPagina == prima));
    }

    [Fact]
    public async Task IReindirizzamentiSonoPerSitoELinguaENonSiEliminanoDaAltrePagine()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "vecchio");
        await s.PubblicaAsync(s.A, id);
        await s.RinominaAsync(s.A, id, "nuovo");
        var altraDiA = await s.CreaPaginaAsync(s.A, s.AdminA, "altra");
        var idRedirect = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!.Reindirizzamenti![0].Id;

        var altraPagina = await s.ComeAsync(s.A, servizio => servizio.EliminaReindirizzamentoAsync(s.AdminA, altraDiA, idRedirect, Ct));
        var altroTenant = await s.ComeAsync(s.B, servizio => servizio.EliminaReindirizzamentoAsync(s.AdminB, id, idRedirect, Ct));
        var attoreAltrui = await s.ComeAsync(s.A, servizio => servizio.EliminaReindirizzamentoAsync(s.AdminB, id, idRedirect, Ct));

        Assert.Equal(CodiceEsito.NonTrovato, altraPagina.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, altroTenant.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, attoreAltrui.Codice);
        Assert.Equal(1, await s.ContaRedirectAsync(r => r.Id == idRedirect));

        var eliminato = await s.ComeAsync(s.A, servizio => servizio.EliminaReindirizzamentoAsync(s.AdminA, id, idRedirect, Ct));
        Assert.True(eliminato.Riuscito);
        Assert.Equal(0, await s.ContaRedirectAsync(r => r.Id == idRedirect));
    }

    [Fact]
    public async Task ICampiSeoSiSalvanoNelleRevisioniEUnaModificaSoloSeoCreaUnaRevisione()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(
            s.AdminA,
            new DatiNuovaPagina("seo", "Titolo", "Testo", null, false, null, null, null, "  Titolo per Google  ", "Descrizione breve", true),
            Ct));
        Assert.True(id.Riuscito, id.Codice.ToString());

        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id.Id!.Value, Ct)))!;
        Assert.Equal("Titolo per Google", letta.TitoloSeo);
        Assert.Equal("Descrizione breve", letta.DescrizioneSeo);
        Assert.True(letta.NonIndicizzare);

        var salvata = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(
            s.AdminA,
            id.Id!.Value,
            new DatiModificaPagina(null, "Titolo", "Testo", null, false, letta.TokenConcorrenza, null, "", null, false),
            Ct));
        Assert.True(salvata.Riuscito);
        letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id.Id!.Value, Ct)))!;
        Assert.Null(letta.TitoloSeo);
        Assert.Null(letta.DescrizioneSeo);
        Assert.False(letta.NonIndicizzare);
        Assert.Equal(2, letta.Revisioni.Count);
    }

    [Theory]
    [InlineData(201, 0, "TitoloSeo")]
    [InlineData(0, 501, "DescrizioneSeo")]
    public async Task ICampiSeoTroppoLunghiSonoRifiutati(int lunghezzaTitolo, int lunghezzaDescrizione, string campo)
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(
            s.AdminA,
            new DatiNuovaPagina("seo", "Titolo", "Testo", null, false, null, null, null, new string('t', lunghezzaTitolo), new string('d', lunghezzaDescrizione)),
            Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal([campo], esito.CampiNonValidi);
    }

    [Fact]
    public async Task BozzaEPubblicatoRestanoSeparatiEIlRitiroTogliLaPagina()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.True((await s.ComeAsync(s.A, servizio => servizio.PubblicaAsync(s.AdminA, id, letta.TokenConcorrenza, Ct))).Riuscito);
        letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(StatoPagina.Pubblicata, letta.Stato);
        Assert.False(letta.ModificheNonPubblicate);

        await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, "Bozza nuova", "Testo nuovo", null, false, letta.TokenConcorrenza), Ct));
        var conBozza = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;

        Assert.Equal(StatoPagina.Pubblicata, conBozza.Stato);
        Assert.True(conBozza.ModificheNonPubblicate);
        Assert.Equal(["Pubblicata"], conBozza.Revisioni.Where(r => r.Pubblicata).Select(_ => "Pubblicata"));
        Assert.Equal(1, conBozza.Revisioni.Single(r => r.Pubblicata).Numero);
        Assert.Equal(2, conBozza.Revisioni.Single(r => r.UltimaBozza).Numero);

        Assert.True((await s.ComeAsync(s.A, servizio => servizio.RitiraAsync(s.AdminA, id, conBozza.TokenConcorrenza, Ct))).Riuscito);
        var ritirata = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(StatoPagina.Bozza, ritirata.Stato);
        Assert.DoesNotContain(ritirata.Revisioni, r => r.Pubblicata);
    }

    [Fact]
    public async Task LaPaginaDiUnAltroTenantOSitoNonSiVedeNeSiModifica()
    {
        await using var s = await Scenario.CreaAsync();
        var idB = await s.CreaPaginaAsync(s.B, s.AdminB, "di-b");
        var secondoSito = await s.AggiungiSitoAsync(s.A);
        var idSecondoSito = await s.CreaPaginaAsync(secondoSito, s.AdminA, "altro-sito");

        // Dall'host A, con un amministratore di A: pagine di B e di un altro sito di A sono come inesistenti.
        Assert.Null(await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, idB, Ct)));
        Assert.Null(await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, idSecondoSito, Ct)));
        var salva = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, idB, new DatiModificaPagina(null, "X", "Y", null, false, Convert.ToBase64String([1, 2, 3])), Ct));
        var pubblica = await s.ComeAsync(s.A, servizio => servizio.PubblicaAsync(s.AdminA, idB, Convert.ToBase64String([1, 2, 3]), Ct));
        var ritira = await s.ComeAsync(s.A, servizio => servizio.RitiraAsync(s.AdminA, idSecondoSito, Convert.ToBase64String([1, 2, 3]), Ct));
        Assert.Equal(CodiceEsito.NonTrovato, salva.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, pubblica.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, ritira.Codice);
        Assert.DoesNotContain(await s.ComeAsync(s.A, servizio => servizio.ElencaAsync(s.AdminA, Ct)), p => p.Slug is "di-b" or "altro-sito");
    }

    [Fact]
    public async Task ChiNonEAmministratoreDelTenantDellHostVieneNegatoEdEInAudit()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        var utente = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.Utente, s.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
        var disabilitato = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.A.IdTenant, "ex@esempio.it", stato: StatoUtenza.Disabilitata, annullamento: Ct);
        var attori = new[] { s.AdminB, utente, disabilitato, Guid.NewGuid() };

        foreach (var attore in attori)
        {
            var crea = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(attore, new DatiNuovaPagina("abusiva", "T", "C", "it", false), Ct));
            var salva = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(attore, id, new DatiModificaPagina(null, "Hacked", "Hacked", null, false, letta.TokenConcorrenza), Ct));
            var pubblica = await s.ComeAsync(s.A, servizio => servizio.PubblicaAsync(attore, id, letta.TokenConcorrenza, Ct));
            Assert.Equal(CodiceEsito.NonAutorizzato, crea.Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, salva.Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, pubblica.Codice);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, servizio => servizio.ElencaAsync(attore, Ct)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, servizio => servizio.LeggiAsync(attore, id, Ct)));
        }

        var dopo = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(letta.Titolo, dopo.Titolo);
        Assert.Equal(StatoPagina.Bozza, dopo.Stato);
        Assert.Equal(1, await s.ContaAsync(contesto => contesto.Pagine.IgnoreQueryFilters().CountAsync(Ct)));
        Assert.Equal(attori.Length * 3, await s.ContaAsync(contesto => contesto.RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata, Ct)));
    }

    [Fact]
    public async Task IlPlatformAdminLavoraSulleProprieDelTenantDellHost()
    {
        await using var s = await Scenario.CreaAsync();
        var platform = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.PlatformAdmin, null, "platform@esempio.it", annullamento: Ct);

        var esito = await s.ComeAsync(s.B, servizio => servizio.CreaAsync(platform, new DatiNuovaPagina("da-platform", "Da platform", "x", "it", false), Ct));

        Assert.True(esito.Riuscito);
        Assert.Single(await s.ComeAsync(s.B, servizio => servizio.ElencaAsync(platform, Ct)));
        Assert.Empty(await s.ComeAsync(s.A, servizio => servizio.ElencaAsync(platform, Ct)));
    }

    private static readonly Dictionary<string, string> _datiPresentazione = new()
    {
        ["Sottotitolo"] = "Un sottotitolo",
        ["EtichettaPulsante"] = "Scopri",
        ["UrlPulsante"] = "/prodotti",
    };

    [Fact]
    public async Task CreaUnaPaginaConUnModelloESalvaIDatiConLaVersione()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("vetrina", "Vetrina", "Testo", "it", false, "presentazione", _datiPresentazione), Ct));

        Assert.True(esito.Riuscito);
        var dettaglio = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, esito.Id!.Value, Ct)))!;
        Assert.Equal("presentazione", dettaglio.IdModello);
        Assert.Equal(1, dettaglio.VersioneModello);
        Assert.Equal(_datiPresentazione, dettaglio.DatiModello);
        Assert.Null(dettaglio.VersioneModelloRecente);
        await s.PubblicaUltimaAsync(s.A, esito.Id!.Value);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(s.A);
        var pubblica = (await ambito.ServiceProvider.GetRequiredService<IServizioPaginePubbliche>().TrovaAsync("vetrina", null, Ct))!;
        Assert.Equal("presentazione", pubblica.IdModello);
        Assert.Equal("Scopri", pubblica.DatiModello["EtichettaPulsante"]);
    }

    [Fact]
    public async Task IDatiDelModelloNonValidiVengonoRifiutatiPerNomeDelCampo()
    {
        await using var s = await Scenario.CreaAsync();
        var pericoloso = new Dictionary<string, string>(_datiPresentazione) { ["UrlPulsante"] = "javascript:alert(1)" };
        var senzaUrl = new Dictionary<string, string> { ["EtichettaPulsante"] = "Vai" };

        var a = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("a", "A", "x", "it", false, "presentazione", pericoloso), Ct));
        var b = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("b", "B", "x", "it", false, "presentazione", senzaUrl), Ct));
        var c = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("c", "C", "x", "it", false, "inesistente", null), Ct));
        var d = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("d", "D", "x", "it", false, "testo-semplice", new Dictionary<string, string> { ["Extra"] = "x" }), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, a.Codice);
        Assert.Equal(["UrlPulsante"], a.CampiNonValidi);
        Assert.Equal(["UrlPulsante"], b.CampiNonValidi);
        Assert.Equal(["Modello"], c.CampiNonValidi);
        Assert.Equal(["Extra"], d.CampiNonValidi);
        Assert.Empty(await s.ComeAsync(s.A, servizio => servizio.ElencaAsync(s.AdminA, Ct)));
    }

    [Fact]
    public async Task SalvareAggiornaIDatiConLaStessaVersioneEUnaRevisioneNonCambiaModelloDaSola()
    {
        await using var s = await Scenario.CreaAsync();
        var id = (await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("vetrina", "Vetrina", "Testo", "it", false, "presentazione", _datiPresentazione), Ct))).Id!.Value;
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;

        var invariata = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, letta.Titolo, letta.Contenuto, null, false, letta.TokenConcorrenza, _datiPresentazione), Ct));
        Assert.True(invariata.Riuscito);
        Assert.Single((await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!.Revisioni);

        var nuovi = new Dictionary<string, string>(_datiPresentazione) { ["Sottotitolo"] = "Nuovo sottotitolo" };
        var token = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!.TokenConcorrenza;
        var modificata = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, letta.Titolo, letta.Contenuto, null, false, token, nuovi), Ct));
        Assert.True(modificata.Riuscito);
        var dopo = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(2, dopo.Revisioni.Count);
        Assert.Equal("Nuovo sottotitolo", dopo.DatiModello["Sottotitolo"]);
        Assert.Equal("presentazione", dopo.IdModello);

        // Campi di un altro modello o valori non validi: rifiutati, nulla cambia.
        var rifiutata = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, letta.Titolo, letta.Contenuto, null, false, dopo.TokenConcorrenza, new Dictionary<string, string> { ["Foto"] = "x" }), Ct));
        Assert.Equal(["Foto"], rifiutata.CampiNonValidi);
        Assert.Equal(2, (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!.Revisioni.Count);
    }

    [Fact]
    public async Task UnaNuovaVersioneDelModelloNonToccaLeRevisioniEsistentiEPuoEssereAdottataDallaBozza()
    {
        await using var s = await Scenario.CreaAsync();
        var id = (await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("vetrina", "Vetrina", "Testo", "it", false, "presentazione", new Dictionary<string, string> { ["Sottotitolo"] = "Sub", ["EtichettaPulsante"] = "Vai", ["UrlPulsante"] = "/x" }), Ct))).Id!.Value;
        await s.PubblicaUltimaAsync(s.A, id);

        // Seconda istanza dell'applicazione sullo stesso database, con la versione 2 del modello (senza il pulsante, con sottotitolo obbligatorio).
        var v2 = RegistroModelliPagina.ModelloPresentazione with
        {
            Versione = 2,
            Campi = [new CampoModello("Sottotitolo", TipoCampoModello.TestoBreve, Obbligatorio: true, LunghezzaMassima: 100)],
            CampiTuttiONessuno = [],
        };
        await using var nuova = await ServiziProva.CreaConMigrazioniAsync(
            s.Connessione,
            configura: servizi => servizi.AddSingleton(new RegistroModelliPagina([.. RegistroModelliPagina.Predefiniti, v2])),
            annullamento: Ct);
        async Task<T> ComeNuova<T>(Func<IServizioPagine, Task<T>> operazione)
        {
            await using var ambito = nuova.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(s.A);
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPagine>());
        }

        var prima = (await ComeNuova(servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(1, prima.VersioneModello);
        Assert.Equal(2, prima.VersioneModelloRecente);

        // Salvare resta sulla versione 1; la versione nuova si adotta solo su richiesta.
        Assert.True((await ComeNuova(servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, prima.Titolo, "Testo modificato", null, false, prima.TokenConcorrenza, prima.DatiModello), Ct))).Riuscito);
        var dopoSalvataggio = (await ComeNuova(servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(1, dopoSalvataggio.VersioneModello);

        var aggiornata = await ComeNuova(servizio => servizio.AggiornaModelloAsync(s.AdminA, id, dopoSalvataggio.TokenConcorrenza, Ct));
        Assert.True(aggiornata.Riuscito);
        var adottata = (await ComeNuova(servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(2, adottata.VersioneModello);
        Assert.Null(adottata.VersioneModelloRecente);
        Assert.Equal(new Dictionary<string, string> { ["Sottotitolo"] = "Sub" }, adottata.DatiModello);
        Assert.Equal(3, adottata.Revisioni.Count);

        // Le revisioni vecchie (anche quella pubblicata) restano alla versione 1, con i loro dati.
        var pubblicata = adottata.Revisioni.Single(r => r.Pubblicata);
        var anteprimaVecchia = (await ComeNuova(servizio => servizio.AnteprimaAsync(s.AdminA, id, pubblicata.Id, Ct)))!;
        Assert.Equal(1, anteprimaVecchia.VersioneModello);
        Assert.Equal("Vai", anteprimaVecchia.DatiModello["EtichettaPulsante"]);

        // Già all'ultima versione: nulla da fare.
        var ancora = await ComeNuova(servizio => servizio.AggiornaModelloAsync(s.AdminA, id, adottata.TokenConcorrenza, Ct));
        Assert.Equal(CodiceEsito.NonConsentito, ancora.Codice);

        // Le nuove pagine nascono con la versione 2 (il sottotitolo è obbligatorio).
        var senza = await ComeNuova(servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("nuova", "Nuova", "x", "it", false, "presentazione", null), Ct));
        Assert.Equal(["Sottotitolo"], senza.CampiNonValidi);
        var con = await ComeNuova(servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("nuova", "Nuova", "x", "it", false, "presentazione", new Dictionary<string, string> { ["Sottotitolo"] = "Ok" }), Ct));
        Assert.Equal(2, (await ComeNuova(servizio => servizio.LeggiAsync(s.AdminA, con.Id!.Value, Ct)))!.VersioneModello);
    }

    [Fact]
    public async Task SeIValoriNonSonoValidiPerLaNuovaVersioneLAggiornamentoNonCambiaNulla()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var v2 = RegistroModelliPagina.ModelloTestoSemplice with
        {
            Versione = 2,
            Campi = [new CampoModello("Autore", TipoCampoModello.TestoBreve, Obbligatorio: true, LunghezzaMassima: 50)],
        };
        await using var nuova = await ServiziProva.CreaConMigrazioniAsync(
            s.Connessione,
            configura: servizi => servizi.AddSingleton(new RegistroModelliPagina([.. RegistroModelliPagina.Predefiniti, v2])),
            annullamento: Ct);
        await using var ambito = nuova.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(s.A);
        var servizio = ambito.ServiceProvider.GetRequiredService<IServizioPagine>();
        var letta = (await servizio.LeggiAsync(s.AdminA, id, Ct))!;

        var esito = await servizio.AggiornaModelloAsync(s.AdminA, id, letta.TokenConcorrenza, Ct);

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal(["Autore"], esito.CampiNonValidi);
        Assert.Single((await servizio.LeggiAsync(s.AdminA, id, Ct))!.Revisioni);
    }

    [Fact]
    public async Task RipristinoEPubblicazioneDiUnaRevisionePassataConservanoModelloEDati()
    {
        await using var s = await Scenario.CreaAsync();
        var id = (await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("vetrina", "Vetrina", "Testo", "it", false, "presentazione", _datiPresentazione), Ct))).Id!.Value;
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina(null, "Altro", "Altro testo", null, false, letta.TokenConcorrenza, new Dictionary<string, string> { ["Sottotitolo"] = "Diverso" }), Ct));
        var dopo = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;

        var ripristino = await s.ComeAsync(s.A, servizio => servizio.RipristinaComeBozzaAsync(s.AdminA, id, dopo.Revisioni.Single(r => r.Numero == 1).Id, dopo.TokenConcorrenza, Ct));

        Assert.True(ripristino.Riuscito);
        var ripristinata = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(_datiPresentazione, ripristinata.DatiModello);
        Assert.Equal("presentazione", ripristinata.IdModello);
    }

    [Fact]
    public async Task LeLingueDelSitoHannoLaPredefinitaPerPrimaEIlCodiceDiDefaultEQuelloDelSito()
    {
        await using var s = await Scenario.CreaAsync();

        var lingue = await s.ComeAsync(s.A, servizio => servizio.ElencaLingueAsync(s.AdminA, Ct));
        var esito = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("pagina", "Pagina", "x", null, false), Ct));

        Assert.Equal(["it", "en"], lingue.Select(l => l.Codice));
        Assert.Equal("it", (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, esito.Id!.Value, Ct)))!.CodiceLingua);
    }

    [Fact]
    public async Task SiCreanoPagineSoloNelleLingueAbilitateDelSito()
    {
        await using var s = await Scenario.CreaAsync();
        await s.RimuoviInglesePerSitoAsync(s.B);

        var inglese = await s.ComeAsync(s.B, servizio => servizio.CreaAsync(s.AdminB, new DatiNuovaPagina("pagina", "Pagina", "x", "en", false), Ct));
        var francese = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("pagina", "Pagina", "x", "fr", false), Ct));
        var abilitata = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("pagina", "Pagina", "x", "en", false), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, inglese.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, francese.Codice);
        Assert.True(abilitata.Riuscito);
        Assert.Equal(["it"], (await s.ComeAsync(s.B, servizio => servizio.ElencaLingueAsync(s.AdminB, Ct))).Select(l => l.Codice));
    }

    [Fact]
    public async Task UnaTraduzioneSiLegaAllaPaginaDOrigineNelloStessoGruppo()
    {
        await using var s = await Scenario.CreaAsync();
        var it = await s.CreaPaginaAsync(s.A, s.AdminA, "chi-siamo");
        var prima = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, it, Ct)))!;
        Assert.Empty(prima.Traduzioni);
        Assert.Equal(["en"], prima.LingueMancanti.Select(l => l.Codice));

        // Stesso slug in una lingua diversa: consentito, perché lo slug è unico per sito e lingua.
        var en = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("chi-siamo", "About us", "x", "en", false, null, null, it), Ct));

        Assert.True(en.Riuscito);
        var dopoIt = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, it, Ct)))!;
        var dopoEn = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, en.Id!.Value, Ct)))!;
        Assert.Equal([("en", "chi-siamo")], dopoIt.Traduzioni.Select(t => (t.CodiceLingua, t.Slug)));
        Assert.Equal([("it", "chi-siamo")], dopoEn.Traduzioni.Select(t => (t.CodiceLingua, t.Slug)));
        Assert.Empty(dopoIt.LingueMancanti);
        Assert.Equal("en", dopoEn.CodiceLingua);
    }

    [Fact]
    public async Task UnaTraduzioneNonSiDuplicaNonUsaPagineAltruiNeSlugRiservatiDalleLingue()
    {
        await using var s = await Scenario.CreaAsync();
        var it = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var idB = await s.CreaPaginaAsync(s.B, s.AdminB, "di-b");
        await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("page", "Page", "x", "en", false, null, null, it), Ct));

        var doppia = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("page-2", "Page 2", "x", "en", false, null, null, it), Ct));
        var stessaLingua = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("pagina-bis", "Bis", "x", "it", false, null, null, it), Ct));
        var altroTenant = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("x", "X", "x", "en", false, null, null, idB), Ct));
        var inesistente = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("y", "Y", "x", "en", false, null, null, Guid.NewGuid()), Ct));
        var slugLingua = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("en", "En", "x", "it", false), Ct));
        var slugIt = await s.ComeAsync(s.A, servizio => servizio.CreaAsync(s.AdminA, new DatiNuovaPagina("IT", "It", "x", "en", false), Ct));

        Assert.Equal(CodiceEsito.GiaPresente, doppia.Codice);
        Assert.Equal(CodiceEsito.GiaPresente, stessaLingua.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, altroTenant.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, inesistente.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, slugLingua.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, slugIt.Codice);
    }

    [Fact]
    public async Task UnoSlugNonPuoDiventareIlCodiceDiUnaLingua()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var letta = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;

        var esito = await s.ComeAsync(s.A, servizio => servizio.SalvaAsync(s.AdminA, id, new DatiModificaPagina("en", letta.Titolo, letta.Contenuto, null, false, letta.TokenConcorrenza), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
    }

    [Fact]
    public async Task LAnteprimaMostraLaBozzaOUnaRevisionePrecisaNonSoloIlPubblicato()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        await s.PubblicaUltimaAsync(s.A, id);
        await s.ModificaAsync(s.A, id, "Titolo bozza", "Testo bozza");
        var dettaglio = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        var pubblicata = dettaglio.Revisioni.Single(r => r.Pubblicata);

        var bozza = await s.ComeAsync(s.A, servizio => servizio.AnteprimaAsync(s.AdminA, id, null, Ct));
        var vecchia = await s.ComeAsync(s.A, servizio => servizio.AnteprimaAsync(s.AdminA, id, pubblicata.Id, Ct));

        Assert.Equal("Testo bozza", bozza!.Contenuto);
        Assert.True(bozza.UltimaBozza);
        Assert.False(bozza.Pubblicata);
        Assert.Equal(2, bozza.NumeroRevisione);
        Assert.Equal("Contenuto", vecchia!.Contenuto);
        Assert.True(vecchia.Pubblicata);
    }

    [Fact]
    public async Task LAnteprimaNonEsponePagineERevisioniDiAltri()
    {
        await using var s = await Scenario.CreaAsync();
        var idA = await s.CreaPaginaAsync(s.A, s.AdminA, "di-a");
        var idB = await s.CreaPaginaAsync(s.B, s.AdminB, "di-b");
        var revisioneB = (await s.ComeAsync(s.B, servizio => servizio.LeggiAsync(s.AdminB, idB, Ct)))!.Revisioni[0].Id;

        Assert.Null(await s.ComeAsync(s.A, servizio => servizio.AnteprimaAsync(s.AdminA, idB, null, Ct)));
        Assert.Null(await s.ComeAsync(s.A, servizio => servizio.AnteprimaAsync(s.AdminA, idA, revisioneB, Ct)));
        Assert.Null(await s.ComeAsync(s.A, servizio => servizio.AnteprimaAsync(s.AdminA, idA, Guid.NewGuid(), Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, servizio => servizio.AnteprimaAsync(s.AdminB, idA, null, Ct)));
    }

    [Fact]
    public async Task IlRollbackPubblicaUnaRevisionePassataSenzaToccareLUltimaBozzaEScriveGliEventi()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        await s.PubblicaUltimaAsync(s.A, id);
        await s.ModificaAsync(s.A, id, "Secondo titolo", "Secondo testo");
        await s.PubblicaUltimaAsync(s.A, id);
        var dettaglio = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        var prima = dettaglio.Revisioni.Single(r => r.Numero == 1);

        var esito = await s.ComeAsync(s.A, servizio => servizio.PubblicaRevisioneAsync(s.AdminA, id, prima.Id, dettaglio.TokenConcorrenza, Ct));

        Assert.True(esito.Riuscito);
        var dopo = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(1, dopo.Revisioni.Single(r => r.Pubblicata).Numero);
        Assert.Equal(2, dopo.Revisioni.Single(r => r.UltimaBozza).Numero);
        Assert.True(dopo.ModificheNonPubblicate);
        Assert.Equal("Secondo testo", dopo.Contenuto);
        Assert.Equal(3, await s.ContaAsync(c => c.MessaggiOutbox.IgnoreQueryFilters().CountAsync(m => m.Tipo == "PaginaPubblicata", Ct)));

        // Anche l'altra direzione: si torna alla seconda revisione.
        var seconda = dopo.Revisioni.Single(r => r.Numero == 2);
        Assert.True((await s.ComeAsync(s.A, servizio => servizio.PubblicaRevisioneAsync(s.AdminA, id, seconda.Id, dopo.TokenConcorrenza, Ct))).Riuscito);
        Assert.False((await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!.ModificheNonPubblicate);
    }

    [Fact]
    public async Task IlRollbackRispettaTokenEAppartenenza()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        var idAltra = await s.CreaPaginaAsync(s.A, s.AdminA, "altra");
        var dettaglio = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        var revisioneAltra = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, idAltra, Ct)))!.Revisioni[0].Id;
        var idB = await s.CreaPaginaAsync(s.B, s.AdminB, "di-b");
        var revisioneB = (await s.ComeAsync(s.B, servizio => servizio.LeggiAsync(s.AdminB, idB, Ct)))!.Revisioni[0].Id;

        var vecchio = await s.ComeAsync(s.A, servizio => servizio.PubblicaRevisioneAsync(s.AdminA, id, dettaglio.Revisioni[0].Id, Convert.ToBase64String([9, 9, 9]), Ct));
        var diAltraPagina = await s.ComeAsync(s.A, servizio => servizio.PubblicaRevisioneAsync(s.AdminA, id, revisioneAltra, dettaglio.TokenConcorrenza, Ct));
        var diAltroTenant = await s.ComeAsync(s.A, servizio => servizio.PubblicaRevisioneAsync(s.AdminA, id, revisioneB, dettaglio.TokenConcorrenza, Ct));
        var ripristinoAltraPagina = await s.ComeAsync(s.A, servizio => servizio.RipristinaComeBozzaAsync(s.AdminA, id, revisioneAltra, dettaglio.TokenConcorrenza, Ct));

        Assert.Equal(CodiceEsito.Conflitto, vecchio.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, diAltraPagina.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, diAltroTenant.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, ripristinoAltraPagina.Codice);
        Assert.Equal(StatoPagina.Bozza, (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!.Stato);
        Assert.Equal(0, await s.ContaAsync(c => c.MessaggiOutbox.IgnoreQueryFilters().CountAsync(Ct)));
    }

    [Fact]
    public async Task RipristinareCreaUnaNuovaBozzaConIlContenutoVecchioSenzaModificareLeRevisioni()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, s.AdminA, "pagina");
        await s.PubblicaUltimaAsync(s.A, id);
        await s.ModificaAsync(s.A, id, "Titolo nuovo", "Testo nuovo");
        var dettaglio = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        var prima = dettaglio.Revisioni.Single(r => r.Numero == 1);

        var esito = await s.ComeAsync(s.A, servizio => servizio.RipristinaComeBozzaAsync(s.AdminA, id, prima.Id, dettaglio.TokenConcorrenza, Ct));

        Assert.True(esito.Riuscito);
        var dopo = (await s.ComeAsync(s.A, servizio => servizio.LeggiAsync(s.AdminA, id, Ct)))!;
        Assert.Equal(3, dopo.Revisioni.Count);
        var nuova = dopo.Revisioni.Single(r => r.UltimaBozza);
        Assert.Equal(3, nuova.Numero);
        Assert.Equal("Ripristino della revisione 1", nuova.Nota);
        Assert.Equal("Titolo pagina", dopo.Titolo);
        Assert.Equal("Contenuto", dopo.Contenuto);
        Assert.Equal(1, dopo.Revisioni.Single(r => r.Pubblicata).Numero);
        var originale = await s.ComeAsync(s.A, servizio => servizio.AnteprimaAsync(s.AdminA, id, prima.Id, Ct));
        Assert.Equal("Contenuto", originale!.Contenuto);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, DatiTenantRisolto a, DatiTenantRisolto b, Guid adminA, Guid adminB)
        {
            _database = database;
            Fornitore = fornitore;
            A = a;
            B = b;
            AdminA = adminA;
            AdminB = adminB;
        }

        public ServiceProvider Fornitore { get; }

        public DatiTenantRisolto A { get; }

        public DatiTenantRisolto B { get; }

        public Guid AdminA { get; }

        public Guid AdminB { get; }

        public static async Task<Scenario> CreaAsync(IEnumerable<ModelloPagina>? modelli = null)
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(
                database.Connessione,
                configura: modelli is null ? null : servizi => servizi.AddSingleton(new RegistroModelliPagina(modelli)),
                annullamento: Ct);
            var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: Ct);
            var adminA = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "anna@esempio.it", annullamento: Ct);
            var adminB = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, b.IdTenant, "bruno@esempio.it", annullamento: Ct);
            return new Scenario(
                database,
                fornitore,
                new DatiTenantRisolto(a.IdTenant, a.IdSito, a.IdDominio, a.Host, false),
                new DatiTenantRisolto(b.IdTenant, b.IdSito, b.IdDominio, b.Host, false),
                adminA,
                adminB);
        }

        /// <summary>Aggiunge un secondo sito (con il suo dominio) al tenant e restituisce il contesto dell'host di quel sito.</summary>
        public async Task<DatiTenantRisolto> AggiungiSitoAsync(DatiTenantRisolto tenant)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var sito = Sito.Crea(tenant.IdTenant, "Secondo sito", null, TimeProvider.System);
            var dominio = DominioSito.CreaVerificato(tenant.IdTenant, sito.Id, "secondo.esempio.it", false, null, TimeProvider.System);
            contesto.Siti.Add(sito);
            contesto.LingueSito.Add(LinguaSito.Crea(tenant.IdTenant, sito.Id, Lingua.IdItaliano, true, TimeProvider.System));
            contesto.LingueSito.Add(LinguaSito.Crea(tenant.IdTenant, sito.Id, Lingua.IdInglese, false, TimeProvider.System));
            contesto.DominiSito.Add(dominio);
            await contesto.SaveChangesAsync(Ct);
            return new DatiTenantRisolto(tenant.IdTenant, sito.Id, dominio.Id, dominio.Host, false);
        }

        public async Task<T> ComeAsync<T>(DatiTenantRisolto host, Func<IServizioPagine, Task<T>> operazione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(host);
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPagine>());
        }

        public async Task<Guid> CreaPaginaAsync(DatiTenantRisolto host, Guid attore, string slug)
        {
            var esito = await ComeAsync(host, servizio => servizio.CreaAsync(attore, new DatiNuovaPagina(slug, $"Titolo {slug}", "Contenuto", "it", false), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public string Connessione => _database.Connessione;

        public async Task RimuoviInglesePerSitoAsync(DatiTenantRisolto host)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().LingueSito.IgnoreQueryFilters()
                .Where(ls => ls.IdSito == host.IdSito && !ls.Predefinita).ExecuteDeleteAsync(Ct);
        }

        public async Task PubblicaAsync(DatiTenantRisolto host, Guid id) => await PubblicaUltimaAsync(host, id);

        public async Task RinominaAsync(DatiTenantRisolto host, Guid id, string slug)
        {
            var dettaglio = (await ComeAsync(host, servizio => servizio.LeggiAsync(AdminA, id, Ct)))!;
            var esito = await ComeAsync(host, servizio => servizio.SalvaAsync(
                AdminA,
                id,
                new DatiModificaPagina(slug, dettaglio.Titolo, dettaglio.Contenuto, null, dettaglio.PaginaIniziale, dettaglio.TokenConcorrenza),
                Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public Task<int> ContaRedirectAsync(System.Linq.Expressions.Expression<Func<RedirectPagina, bool>> condizione) =>
            ContaAsync(contesto => contesto.RedirectPagine.IgnoreQueryFilters().CountAsync(condizione, Ct));

        public Task<int> ContaOutboxAsync(string tipo) =>
            ContaAsync(contesto => contesto.MessaggiOutbox.IgnoreQueryFilters().CountAsync(m => m.Tipo == tipo, Ct));

        public async Task PubblicaUltimaAsync(DatiTenantRisolto host, Guid id)
        {
            var dettaglio = (await ComeAsync(host, servizio => servizio.LeggiAsync(AdminA, id, Ct)))!;
            var esito = await ComeAsync(host, servizio => servizio.PubblicaAsync(AdminA, id, dettaglio.TokenConcorrenza, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task ModificaAsync(DatiTenantRisolto host, Guid id, string titolo, string contenuto)
        {
            var dettaglio = (await ComeAsync(host, servizio => servizio.LeggiAsync(AdminA, id, Ct)))!;
            var esito = await ComeAsync(host, servizio => servizio.SalvaAsync(AdminA, id, new DatiModificaPagina(null, titolo, contenuto, null, dettaglio.PaginaIniziale, dettaglio.TokenConcorrenza), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task<T> ContaAsync<T>(Func<ContestoDatabase, Task<T>> lettura)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await lettura(ambito.ServiceProvider.GetRequiredService<ContestoDatabase>());
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}

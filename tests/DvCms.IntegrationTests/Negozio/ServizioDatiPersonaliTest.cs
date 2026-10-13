using System.Text.Json;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Dati personali degli acquirenti (A7): esportazione, anonimizzazione dell'account e degli ordini a fine conservazione, permessi e isolamento.</summary>
public class ServizioDatiPersonaliTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Scenario(ContestoNegozio C, Guid Variante, Guid Ritiro, Guid Pagato, Guid DaPagare);

    /// <summary>Il cliente ha un ordine pagato (10 EUR), uno non pagato che trattiene 2 pezzi di giacenza e un carrello.</summary>
    private static async Task<Scenario> CreaAsync()
    {
        var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 10);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var pagato = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        await c.ImpostaStatoAsync(pagato, StatoOrdine.Pagato);
        await c.AggiungiAsync(c.Cliente, varianti[0], 2);
        var daPagare = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 20m), Ct))).Id!.Value;
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        return new Scenario(c, varianti[0], ritiro, pagato, daPagare);
    }

    [Fact]
    public async Task LEsportazioneContieneAccountEOrdiniDelSoloInteressato()
    {
        var s = await CreaAsync();
        await using var _ = s.C;

        var mia = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.EsportaMieiAsync(s.C.Cliente, Ct));
        var altrui = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.EsportaMieiAsync(s.C.ClienteDiB, Ct));
        var amministratore = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.EsportaMieiAsync(s.C.AdminA, Ct));

        Assert.NotNull(mia);
        using var documento = JsonDocument.Parse(mia.Json);
        Assert.Equal("cliente@esempio.it", documento.RootElement.GetProperty("Account").GetProperty("Email").GetString());
        var ordini = documento.RootElement.GetProperty("Ordini");
        Assert.Equal(2, ordini.GetArrayLength());
        Assert.Equal("LIB-1", ordini[0].GetProperty("Righe")[0].GetProperty("Sku").GetString());
        Assert.Null(altrui);
        Assert.Null(amministratore);
    }

    [Fact]
    public async Task LAnonimizzazioneVuolePasswordCorrettaEUnAmministratoreNonSiCancellaDaQui()
    {
        var s = await CreaAsync();
        await using var _ = s.C;

        var errata = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaMioAccountAsync(s.C.Cliente, "password-sbagliata", Ct));
        var vuota = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaMioAccountAsync(s.C.Cliente, null, Ct));
        var amministratore = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaMioAccountAsync(s.C.AdminA, DatiProva.PasswordProva, Ct));
        var altroTenant = await s.C.ComeDatiPersonaliAsync(s.C.B, d => d.AnonimizzaMioAccountAsync(s.C.Cliente, DatiProva.PasswordProva, Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, errata.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, vuota.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, amministratore.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, altroTenant.Codice);
        var utenza = await s.C.UtenzaAsync(s.C.Cliente);
        Assert.Equal((StatoUtenza.Attiva, "cliente@esempio.it", 2), (utenza.Stato, utenza.Email, utenza.ContatoreAccessiFalliti));
        Assert.Equal(StatoUtenza.Attiva, (await s.C.UtenzaAsync(s.C.AdminA)).Stato);
    }

    [Fact]
    public async Task LAnonimizzazioneCancellaLAccountAnnullaGliOrdiniNonPagatiELiberaLaGiacenzaMaConservaQuelliPagati()
    {
        var s = await CreaAsync();
        await using var _ = s.C;
        // Pezzi trattenuti: 1 dell'ordine pagato (la prova lo segna pagato senza passare dal provider) e 2 di quello da pagare.
        Assert.Equal(3, (await s.C.GiacenzaAsync(s.Variante)).Riservata);

        var esito = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaMioAccountAsync(s.C.Cliente, DatiProva.PasswordProva, Ct));

        Assert.Equal(CodiceEsito.Riuscito, esito.Codice);
        var utenza = await s.C.UtenzaAsync(s.C.Cliente);
        Assert.Equal(StatoUtenza.Anonimizzata, utenza.Stato);
        Assert.DoesNotContain("cliente", utenza.Email, StringComparison.Ordinal);
        Assert.DoesNotContain("cliente", utenza.EmailNormalizzata, StringComparison.Ordinal);
        Assert.Equal("Account eliminato", utenza.NomeVisualizzato);
        Assert.False(utenza.DueFattoriAttivi);
        Assert.Equal(StatoOrdine.Annullato, await s.C.StatoOrdineAsync(s.DaPagare));
        Assert.Equal(1, (await s.C.GiacenzaAsync(s.Variante)).Riservata);
        var pagato = await s.C.OrdineAsync(s.Pagato);
        Assert.Equal((StatoOrdine.Pagato, "cliente@esempio.it", 10m, null), (pagato.Stato, pagato.EmailAcquirente, pagato.TotaleLordo, pagato.AnonimizzatoIlUtc));
        await using (var ambito = s.C.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            Assert.Equal(0, await contesto.Carrelli.IgnoreQueryFilters().CountAsync(k => k.IdUtenza == s.C.Cliente, Ct));
        }

        Assert.Null(await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.EsportaMieiAsync(s.C.Cliente, Ct)));
        Assert.Equal(CodiceEsito.NonAutorizzato, (await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaMioAccountAsync(s.C.Cliente, DatiProva.PasswordProva, Ct))).Codice);
        // L'altro cliente e l'omonimo dell'altro tenant non sono toccati.
        Assert.Equal(StatoUtenza.Attiva, (await s.C.UtenzaAsync(s.C.ClienteDiB)).Stato);
        Assert.Equal(StatoUtenza.Attiva, (await s.C.UtenzaAsync(s.C.AltroCliente)).Stato);
    }

    [Fact]
    public async Task UnAmministratoreAnonimizzaLAcquirenteDiUnOrdineDelProprioTenantEBasta()
    {
        var s = await CreaAsync();
        await using var _ = s.C;

        var comeCliente = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaAcquirenteDiOrdineAsync(s.C.AltroCliente, s.Pagato, Ct));
        var comeAltroTenant = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaAcquirenteDiOrdineAsync(s.C.AdminB, s.Pagato, Ct));
        var ordineInesistente = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaAcquirenteDiOrdineAsync(s.C.AdminA, Guid.NewGuid(), Ct));
        var daB = await s.C.ComeDatiPersonaliAsync(s.C.B, d => d.AnonimizzaAcquirenteDiOrdineAsync(s.C.AdminB, s.Pagato, Ct));
        Assert.Equal(StatoUtenza.Attiva, (await s.C.UtenzaAsync(s.C.Cliente)).Stato);

        var riuscito = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaAcquirenteDiOrdineAsync(s.C.AdminA, s.Pagato, Ct));
        var ripetuto = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaAcquirenteDiOrdineAsync(s.C.AdminA, s.Pagato, Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, comeCliente.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAltroTenant.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, ordineInesistente.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, daB.Codice);
        Assert.Equal(CodiceEsito.Riuscito, riuscito.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, ripetuto.Codice);
        Assert.Equal(StatoUtenza.Anonimizzata, (await s.C.UtenzaAsync(s.C.Cliente)).Stato);
        Assert.Equal(StatoOrdine.Annullato, await s.C.StatoOrdineAsync(s.DaPagare));
    }

    [Fact]
    public async Task UnAccountAnonimizzatoNonSiRiattivaDalPannelloDellaPiattaforma()
    {
        var s = await CreaAsync();
        await using var _ = s.C;
        Assert.Equal(CodiceEsito.Riuscito, (await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaMioAccountAsync(s.C.Cliente, DatiProva.PasswordProva, Ct))).Codice);

        EsitoOperazione esito;
        await using (var ambito = s.C.Web.Fabbrica.Services.CreateAsyncScope())
        {
            esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaUtenzaAttivaAsync(s.C.Platform, s.C.A.IdTenant, s.C.Cliente, true, Ct);
        }

        Assert.Equal(CodiceEsito.NonConsentito, esito.Codice);
        Assert.Equal(StatoUtenza.Anonimizzata, (await s.C.UtenzaAsync(s.C.Cliente)).Stato);
    }

    [Fact]
    public async Task LaManutenzioneAnonimizzaLaCopiaPersonaleDegliOrdiniOltreLaConservazioneEConservaIDatiContabili()
    {
        var s = await CreaAsync();
        await using var _ = s.C;
        await s.C.InvecchiaOrdineAsync(s.Pagato, DateTime.UtcNow.AddYears(-11));

        var trattati = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaOrdiniScadutiAsync(Ct));
        var ripetuto = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaOrdiniScadutiAsync(Ct));

        Assert.Equal((1, 0), (trattati, ripetuto));
        var vecchio = await s.C.OrdineAsync(s.Pagato);
        Assert.NotNull(vecchio.AnonimizzatoIlUtc);
        Assert.Equal(("anonimo@anonimizzato.invalid", "Account eliminato", null, null), (vecchio.EmailAcquirente, vecchio.NomeAcquirente, vecchio.IndirizzoRiga1, vecchio.Citta));
        Assert.Equal((StatoOrdine.Pagato, 10m, 1), (vecchio.Stato, vecchio.TotaleLordo, vecchio.Numero));
        var recente = await s.C.OrdineAsync(s.DaPagare);
        Assert.Equal((null, "cliente@esempio.it"), (recente.AnonimizzatoIlUtc, recente.EmailAcquirente));
        // Le righe dello snapshot (prodotti e importi) restano.
        var esportazione = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.EsportaMieiAsync(s.C.Cliente, Ct));
        Assert.Contains("LIB-1", esportazione!.Json, StringComparison.Ordinal);
    }
}

using DvCms.Contracts.Accessi;
using DvCms.Contracts.Accessi.DueFattori;
using DvCms.Contracts.Builder;
using DvCms.Contracts.Cms;
using DvCms.Contracts.Email;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Media;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Outbox;
using DvCms.Contracts.Persistenza;
using DvCms.Contracts.Piattaforma;
using DvCms.Contracts.Temi;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Builder;
using DvCms.Core.Cache;
using DvCms.Core.Cms;
using DvCms.Core.Localizzazione;
using DvCms.Core.Media;
using DvCms.Core.Segreti;
using DvCms.Core.Temi;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.Data.Cache;
using DvCms.Data.Outbox;
using DvCms.Data.Popolamento;
using DvCms.Data.Segreti;
using DvCms.Data.Tenancy;
using DvCms.DTO.Accessi;
using DvCms.DTO.Accessi.DueFattori;
using DvCms.DTO.Cache;
using DvCms.DTO.Manutenzione;
using DvCms.DTO.Media;
using DvCms.DTO.Negozio;
using DvCms.DTO.Outbox;
using DvCms.Infrastructure.Accessi;
using DvCms.Infrastructure.Cache;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Localizzazione;
using DvCms.Infrastructure.Manutenzione;
using DvCms.Infrastructure.Media;
using DvCms.Infrastructure.Negozio;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Pagamenti;
using DvCms.Infrastructure.Piattaforma;
using DvCms.Infrastructure.Temi;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DvCms.Infrastructure;

public static class EstensioniServizi
{
    /// <summary>
    /// Registra i servizi di infrastruttura. Il database lo registra il progetto del provider (<c>AggiungiSqlServer</c>, ADR-0024).
    /// </summary>
    public static IServiceCollection AggiungiInfrastruttura(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        AggiungiCache(servizi, configurazione);

        AggiungiAccessi(servizi, configurazione);
        AggiungiLocalizzazione(servizi);
        AggiungiOutbox(servizi, configurazione);

        return servizi;
    }

    private static void AggiungiAccessi(IServiceCollection servizi, IConfiguration configurazione)
    {
        var opzioni = LeggiOpzioniSicurezza(configurazione);
        servizi.TryAddSingleton(opzioni);
        servizi.TryAddSingleton<IHashPassword, HashPasswordBCrypt>();
        servizi.TryAddSingleton<HashFinto>();
        servizi.TryAddScoped<IServizioAudit, ServizioAudit>();
        servizi.TryAddSingleton(LeggiOpzioniDueFattori(configurazione));
        servizi.TryAddScoped<ContatoreTentativi>();
        servizi.TryAddScoped<IServizioAccesso, ServizioAccesso>();
        servizi.TryAddScoped<IServizioDueFattori, ServizioDueFattori>();
        servizi.TryAddSingleton(LeggiOpzioniRecuperoPassword(configurazione));
        servizi.TryAddScoped<IServizioRecuperoPassword, ServizioRecuperoPassword>();
        servizi.TryAddSingleton(new OpzioniInviti(TimeSpan.FromHours(LeggiIntero(configurazione, "Sicurezza:Inviti:OreValidita", (int)OpzioniInviti.Predefinite.Validita.TotalHours, 1, 720))));
        servizi.TryAddScoped<IServizioInviti, ServizioInviti>();
        servizi.TryAddSingleton(new OpzioniAcquirenti(
            TimeSpan.FromHours(LeggiIntero(configurazione, "Sicurezza:Acquirenti:OreValiditaConferma", (int)OpzioniAcquirenti.Predefinite.ValiditaConferma.TotalHours, 1, 168)),
            LeggiIntero(configurazione, "Sicurezza:Acquirenti:RichiesteOrarieMassimeConferma", OpzioniAcquirenti.Predefinite.RichiesteOrarieMassimeConferma, 1, 20),
            TimeSpan.FromDays(LeggiIntero(configurazione, "Sicurezza:Acquirenti:GiorniConservazioneNonConfermati", (int)OpzioniAcquirenti.Predefinite.ConservazioneNonConfermati.TotalDays, 1, 90))));
        servizi.TryAddScoped<IServizioAcquirenti, ServizioAcquirenti>();
        servizi.TryAddScoped<ServizioPopolamento>();
        servizi.TryAddScoped<ServizioSitemap>();
        servizi.TryAddScoped<IServizioSitemap>(f => new ServizioSitemapConCache(f.GetRequiredService<ServizioSitemap>(), f.GetRequiredService<ICachePubblica>(), f.GetRequiredService<IContestoTenant>()));
        servizi.TryAddSingleton(new RegistroTemi(RegistroTemi.Predefiniti));
        servizi.TryAddScoped<IServizioTemi, ServizioTemi>();
        servizi.TryAddScoped<RisolutoreTema>();
        servizi.TryAddScoped<IRisolutoreTema>(f => new RisolutoreTemaConCache(f.GetRequiredService<RisolutoreTema>(), f.GetRequiredService<ICachePubblica>(), f.GetRequiredService<IContestoTenant>()));
        AggiungiMedia(servizi, configurazione);
        servizi.TryAddScoped<IServizioPiattaforma, ServizioPiattaforma>();
        servizi.TryAddSingleton(new RegistroModelliPagina(RegistroModelliPagina.Predefiniti));
        servizi.TryAddSingleton(new RegistroComponenti(RegistroComponenti.Predefiniti));
        servizi.TryAddSingleton(new RegistroTemplateBuilder(RegistroTemplateBuilder.Predefiniti));
        servizi.TryAddScoped<IServizioBuilder, ServizioBuilder>();
        servizi.TryAddScoped<IServizioPagine, ServizioPagine>();
        servizi.TryAddScoped<ServizioPaginePubbliche>();
        servizi.TryAddScoped<IServizioPaginePubbliche>(f => new ServizioPaginePubblicheConCache(f.GetRequiredService<ServizioPaginePubbliche>(), f.GetRequiredService<ICachePubblica>(), f.GetRequiredService<IContestoTenant>()));
    }

    private static void AggiungiOutbox(IServiceCollection servizi, IConfiguration configurazione)
    {
        var predefinite = OpzioniOutbox.Predefinite;
        var abilitato = true;
        if (!string.IsNullOrWhiteSpace(configurazione["Outbox:Abilitato"]) && !bool.TryParse(configurazione["Outbox:Abilitato"], out abilitato))
        {
            throw new InvalidOperationException("Valore non valido per 'Outbox:Abilitato': atteso true o false.");
        }

        var opzioni = new OpzioniOutbox(
            abilitato,
            TimeSpan.FromSeconds(LeggiIntero(configurazione, "Outbox:IntervalloSecondi", (int)predefinite.Intervallo.TotalSeconds, 1, 3600)),
            LeggiIntero(configurazione, "Outbox:DimensioneLotto", predefinite.DimensioneLotto, 1, 500),
            LeggiIntero(configurazione, "Outbox:TentativiMassimi", predefinite.TentativiMassimi, 1, 50),
            predefinite.DurataPrenotazione);
        servizi.TryAddSingleton(opzioni);
        servizi.TryAddScoped<IOutbox, ScrittoreOutbox>();
        servizi.TryAddScoped<IGestioneCodaOutbox, ServizioCodaOutbox>();
        AggiungiManutenzione(servizi, configurazione);
        servizi.TryAddSingleton<ElaboratoreOutbox>();
        servizi.AddScoped<IGestoreOutbox, GestorePaginaPubblicata>();
        servizi.AddScoped<IGestoreOutbox, GestorePaginaRitirata>();
        servizi.AddScoped<IGestoreOutbox, GestorePaginaRinominata>();
        servizi.AddHostedService<ServizioOutbox>();
    }

    private static void AggiungiCache(IServiceCollection servizi, IConfiguration configurazione)
    {
        var p = OpzioniCache.Predefinite;
        var abilitata = true;
        if (!string.IsNullOrWhiteSpace(configurazione["Cache:Abilitata"]) && !bool.TryParse(configurazione["Cache:Abilitata"], out abilitata))
        {
            throw new InvalidOperationException("Valore non valido per 'Cache:Abilitata': atteso true o false.");
        }

        servizi.TryAddSingleton(new OpzioniCache(
            abilitata,
            TimeSpan.FromSeconds(LeggiIntero(configurazione, "Cache:SecondiValidita", (int)p.Durata.TotalSeconds, 1, 86_400)),
            LeggiIntero(configurazione, "Cache:VociMassime", p.VociMassime, 10, 1_000_000),
            TimeSpan.FromSeconds(LeggiIntero(configurazione, "Cache:IntervalloLetturaSecondi", (int)p.IntervalloLettura.TotalSeconds, 1, 600))));
        servizi.TryAddSingleton<ICachePubblica, CachePubblica>();
        servizi.TryAddSingleton<InterceptorInvalidazioneCache>();
        servizi.TryAddScoped<IInvalidazioneCache, ScrittoreInvalidazioneCache>();
        servizi.TryAddSingleton<ServizioInvalidazioneCache>();
        servizi.AddHostedService(fornitore => fornitore.GetRequiredService<ServizioInvalidazioneCache>());
    }

    private static void AggiungiManutenzione(IServiceCollection servizi, IConfiguration configurazione)
    {
        var p = OpzioniManutenzione.Predefinite;
        var abilitata = true;
        if (!string.IsNullOrWhiteSpace(configurazione["Manutenzione:Abilitata"]) && !bool.TryParse(configurazione["Manutenzione:Abilitata"], out abilitata))
        {
            throw new InvalidOperationException("Valore non valido per 'Manutenzione:Abilitata': atteso true o false.");
        }

        servizi.TryAddSingleton(new OpzioniManutenzione(
            abilitata,
            TimeSpan.FromMinutes(LeggiIntero(configurazione, "Manutenzione:IntervalloMinuti", (int)p.Intervallo.TotalMinutes, 1, 10_080)),
            TimeSpan.FromDays(LeggiIntero(configurazione, "Manutenzione:GiorniConservazioneCompletati", (int)p.ConservazioneCompletati.TotalDays, 1, 3650)),
            TimeSpan.FromDays(LeggiIntero(configurazione, "Manutenzione:GiorniConservazioneFalliti", (int)p.ConservazioneFalliti.TotalDays, 1, 3650)),
            TimeSpan.FromDays(LeggiIntero(configurazione, "Manutenzione:GiorniConservazioneAccessi", (int)p.ConservazioneAccessi.TotalDays, 1, 3650)),
            LeggiIntero(configurazione, "Manutenzione:DimensioneLotto", p.DimensioneLotto, 1, 10_000)));
        servizi.TryAddSingleton<ManutenzioneDati>();
        servizi.AddHostedService<ServizioManutenzione>();
    }

    private static void AggiungiMedia(IServiceCollection servizi, IConfiguration configurazione)
    {
        var p = OpzioniMedia.Predefinite;
        servizi.TryAddSingleton(new OpzioniMedia(
            LeggiIntero(configurazione, "Media:DimensioneMassimaKb", (int)(p.DimensioneMassima / 1024), 1, 102_400) * 1024L,
            LeggiIntero(configurazione, "Media:LatoMassimoImmagine", p.LatoMassimoImmagine, 1, 65_535),
            LeggiIntero(configurazione, "Media:PixelMassimiMilioni", (int)(p.PixelMassimi / 1_000_000), 1, 1000) * 1_000_000L));

        // Archivio degli oggetti: provider scelto da configurazione; uno sconosciuto ferma l'avvio invece di ripiegare su altro.
        var provider = configurazione["Archivio:Provider"] ?? "FileSystem";
        switch (provider)
        {
            case "FileSystem":
                var radice = configurazione["Archivio:Percorso"];
                servizi.TryAddSingleton<IArchivioOggetti>(_ => new ArchivioFileSystem(
                    string.IsNullOrWhiteSpace(radice) ? Path.Combine(AppContext.BaseDirectory, "archivio") : radice));
                break;
            default:
                throw new InvalidOperationException($"Provider dell'archivio '{provider}' non riconosciuto (valori ammessi: FileSystem).");
        }

        servizi.TryAddScoped<IServizioMedia, ServizioMedia>();
        servizi.TryAddScoped<IServizioCatalogo, ServizioCatalogo>();
        servizi.TryAddScoped<IServizioSpedizioni, ServizioSpedizioni>();
        servizi.TryAddScoped<IServizioPagamentiOffline, ServizioPagamentiOffline>();
        servizi.TryAddScoped<IServizioCarrello, ServizioCarrello>();
        servizi.TryAddScoped<IServizioVetrina, ServizioVetrina>();
        servizi.TryAddScoped<IServizioCategorie, ServizioCategorie>();
        servizi.TryAddScoped<IServizioOrdini, ServizioOrdini>();
        servizi.TryAddScoped<IServizioEsportazioneOrdini, ServizioEsportazioneOrdini>();
        servizi.TryAddScoped<IServizioGiacenze, ServizioGiacenze>();
        servizi.AddHttpClient(ClientePayPal.NomeClientHttp, client => client.Timeout = Timeout.InfiniteTimeSpan);
        servizi.TryAddScoped<IProviderPagamento, ClientePayPal>();
        servizi.AddHttpClient(ClienteNexi.NomeClientHttp, client => client.Timeout = Timeout.InfiniteTimeSpan);
        servizi.TryAddScoped<ClienteNexi>();
        servizi.TryAddScoped<ServizioImpostazioniNexi>();
        servizi.TryAddScoped<IServizioImpostazioniNexi>(f => f.GetRequiredService<ServizioImpostazioniNexi>());
        servizi.AddScoped<IGatewayPagamento, GatewayPayPal>();
        servizi.AddScoped<IGatewayPagamento, GatewayNexi>();
        servizi.TryAddSingleton(new OpzioniNegozio(configurazione.GetValue<bool>("Negozio:CheckoutProduzione"), configurazione.GetValue("Negozio:AvvisoNuovoOrdineAmministratori", true)));
        servizi.TryAddScoped<NotificheOrdine>();
        servizi.TryAddScoped<IServizioRimborsi, ServizioRimborsi>();
        servizi.TryAddSingleton(new OpzioniDatiPersonali(LeggiIntero(configurazione, "Negozio:ConservazioneOrdiniAnni", OpzioniDatiPersonali.Predefinite.ConservazioneOrdiniAnni, 1, 30)));
        servizi.TryAddScoped<IServizioDatiPersonali, ServizioDatiPersonali>();
        servizi.TryAddScoped<IServizioPagamenti, ServizioPagamenti>();
        servizi.TryAddScoped<RiconciliazionePagamenti>();
        servizi.TryAddScoped<IServizioProntezzaNegozio, ServizioProntezzaNegozio>();
        servizi.TryAddScoped<ServizioImpostazioniPagamento>();
        servizi.TryAddScoped<IServizioImpostazioniPagamento>(f => f.GetRequiredService<ServizioImpostazioniPagamento>());
        servizi.TryAddScoped<IArchivioCredenzialiPagamento>(f => f.GetRequiredService<ServizioImpostazioniPagamento>());
        servizi.TryAddScoped<VerificatoreArchivio>();
    }

    private static OpzioniRecuperoPassword LeggiOpzioniRecuperoPassword(IConfiguration configurazione)
    {
        var p = OpzioniRecuperoPassword.Predefinite;
        return new OpzioniRecuperoPassword(
            TimeSpan.FromMinutes(LeggiIntero(configurazione, "Sicurezza:RecuperoPassword:MinutiValiditaCodice", (int)p.ValiditaCodice.TotalMinutes, 1, 1440)),
            LeggiIntero(configurazione, "Sicurezza:RecuperoPassword:TentativiCodice", p.TentativiCodice, 1, 20),
            TimeSpan.FromMinutes(LeggiIntero(configurazione, "Sicurezza:RecuperoPassword:MinutiValiditaLink", (int)p.ValiditaLink.TotalMinutes, 1, 1440)),
            LeggiIntero(configurazione, "Sicurezza:RecuperoPassword:RichiesteOrarieAccount", p.RichiesteOrarieAccount, 1, 100),
            LeggiIntero(configurazione, "Sicurezza:RecuperoPassword:RichiesteOrarieIp", p.RichiesteOrarieIp, 1, 1000));
    }

    private static OpzioniDueFattori LeggiOpzioniDueFattori(IConfiguration configurazione)
    {
        var obbligo = OpzioniDueFattori.Predefinite.Obbligo;
        var testo = configurazione["Sicurezza:DueFattori:Obbligo"];
        if (!string.IsNullOrWhiteSpace(testo)
            && (!Enum.TryParse(testo.Trim(), ignoreCase: true, out obbligo) || !Enum.IsDefined(obbligo)))
        {
            throw new InvalidOperationException("Valore non valido per 'Sicurezza:DueFattori:Obbligo': atteso Nessuno, PlatformAdmin o Amministratori.");
        }

        var emittente = configurazione["Sicurezza:DueFattori:Emittente"];
        return new OpzioniDueFattori(obbligo, string.IsNullOrWhiteSpace(emittente) ? OpzioniDueFattori.Predefinite.Emittente : emittente.Trim());
    }

    private static OpzioniSicurezza LeggiOpzioniSicurezza(IConfiguration configurazione)
    {
        var fattore = LeggiIntero(configurazione, "Sicurezza:Password:FattoreLavoroBcrypt", OpzioniSicurezza.FattoreLavoroPredefinito, 4, 31);
        var tentativi = LeggiIntero(configurazione, "Sicurezza:Accesso:TentativiMassimi", OpzioniSicurezza.TentativiMassimiPredefiniti, 1, 100);
        var minutiBlocco = LeggiIntero(configurazione, "Sicurezza:Accesso:MinutiBlocco", OpzioniSicurezza.MinutiBloccoPredefiniti, 1, 1440);
        return new OpzioniSicurezza(fattore, tentativi, TimeSpan.FromMinutes(minutiBlocco));
    }

    private static int LeggiIntero(IConfiguration configurazione, string chiave, int predefinito, int minimo, int massimo)
    {
        var testo = configurazione[chiave];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return predefinito;
        }

        if (!int.TryParse(testo, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var valore)
            || valore < minimo
            || valore > massimo)
        {
            throw new InvalidOperationException($"Valore non valido per '{chiave}': atteso un intero tra {minimo} e {massimo}.");
        }

        return valore;
    }

    private static void AggiungiLocalizzazione(IServiceCollection servizi)
    {
        servizi.TryAddSingleton<CacheTestiStatici>();
        servizi.TryAddSingleton<CodaTestiMancanti>();
        servizi.TryAddSingleton<ElaboratoreTestiMancanti>();
        servizi.TryAddSingleton<IArchivioTestiStatici, ArchivioTestiStatici>();
        servizi.TryAddScoped<IContestoLingua, ContestoLinguaFisso>();
        servizi.TryAddScoped<ILocalizzatore, Localizzatore>();
        servizi.TryAddScoped<IServizioGestioneTesti, ServizioGestioneTesti>();
        servizi.AddHostedService<ServizioTestiStatici>();
    }

}

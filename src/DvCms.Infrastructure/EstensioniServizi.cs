using DvCms.Application.Accessi;
using DvCms.Application.Cms;
using DvCms.Application.Email;
using DvCms.Application.Localizzazione;
using DvCms.Application.Media;
using DvCms.Application.Outbox;
using DvCms.Application.Piattaforma;
using DvCms.Application.Temi;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Accessi;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Email;
using DvCms.Infrastructure.Localizzazione;
using DvCms.Infrastructure.Manutenzione;
using DvCms.Infrastructure.Media;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.Infrastructure.Piattaforma;
using DvCms.Infrastructure.Popolamento;
using DvCms.Infrastructure.Segreti;
using DvCms.Infrastructure.Temi;
using DvCms.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DvCms.Infrastructure;

public static class EstensioniServizi
{
    public const string NomeConnessionePredefinita = "Predefinita";

    /// <summary>
    /// Registra persistenza (SQL Server) e servizi di infrastruttura. La stringa di connessione deve arrivare da
    /// configurazione di ambiente, user-secrets o variabile <c>ConnectionStrings__Predefinita</c>, mai dal repository:
    /// se manca l'avvio fallisce subito.
    /// </summary>
    public static IServiceCollection AggiungiInfrastruttura(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        var connessione = configurazione.GetConnectionString(NomeConnessionePredefinita);
        if (string.IsNullOrWhiteSpace(connessione))
        {
            throw new InvalidOperationException(
                $"Stringa di connessione 'ConnectionStrings:{NomeConnessionePredefinita}' mancante: " +
                "impostarla con user-secrets o con la variabile d'ambiente ConnectionStrings__Predefinita.");
        }

        servizi.AddDbContext<ContestoDatabase>(opzioni => opzioni.UseSqlServer(connessione, ConfiguraSqlServer));
        servizi.TryAddSingleton(TimeProvider.System);

        servizi.TryAddScoped<ContestoTenant>();
        servizi.TryAddScoped<IContestoTenant>(fornitore => fornitore.GetRequiredService<ContestoTenant>());
        servizi.TryAddScoped<IRisolutoreTenant, RisolutoreTenantDatabase>();

        AggiungiAccessi(servizi, configurazione);
        AggiungiLocalizzazione(servizi);
        AggiungiOutbox(servizi, configurazione);
        AggiungiEmail(servizi, configurazione);

        return servizi;
    }

    private static void AggiungiAccessi(IServiceCollection servizi, IConfiguration configurazione)
    {
        var opzioni = LeggiOpzioniSicurezza(configurazione);
        servizi.TryAddSingleton(opzioni);
        servizi.TryAddSingleton<IHashPassword, HashPasswordBCrypt>();
        servizi.TryAddSingleton<HashFinto>();
        servizi.TryAddScoped<IServizioAudit, ServizioAudit>();
        servizi.TryAddScoped<IServizioAccesso, ServizioAccesso>();
        servizi.TryAddSingleton(LeggiOpzioniRecuperoPassword(configurazione));
        servizi.TryAddScoped<IServizioRecuperoPassword, ServizioRecuperoPassword>();
        servizi.TryAddSingleton(new OpzioniInviti(TimeSpan.FromHours(LeggiIntero(configurazione, "Sicurezza:Inviti:OreValidita", (int)OpzioniInviti.Predefinite.Validita.TotalHours, 1, 720))));
        servizi.TryAddScoped<IServizioInviti, ServizioInviti>();
        servizi.TryAddScoped<ServizioPopolamento>();
        servizi.TryAddScoped<IServizioSitemap, ServizioSitemap>();
        servizi.TryAddSingleton(new RegistroTemi(RegistroTemi.Predefiniti));
        servizi.TryAddScoped<IServizioTemi, ServizioTemi>();
        servizi.TryAddScoped<IRisolutoreTema, RisolutoreTema>();
        AggiungiMedia(servizi, configurazione);
        servizi.TryAddScoped<IServizioPiattaforma, ServizioPiattaforma>();
        servizi.TryAddSingleton(new RegistroModelliPagina(RegistroModelliPagina.Predefiniti));
        servizi.TryAddScoped<IServizioPagine, ServizioPagine>();
        servizi.TryAddScoped<IServizioPaginePubbliche, ServizioPaginePubbliche>();
    }

    private static void AggiungiEmail(IServiceCollection servizi, IConfiguration configurazione)
    {
        servizi.TryAddSingleton(new ChiaveMaestraSegreti(configurazione[ChiaveMaestraSegreti.NomeConfigurazione]));
        servizi.TryAddSingleton<IProtettoreSegreti, ProtettoreSegreti>();
        servizi.AddScoped<IAdapterEmail, AdapterSmtp>();
        servizi.AddHttpClient(ComunicazioneEmail.NomeClientHttp, client => client.Timeout = Timeout.InfiniteTimeSpan);
        servizi.AddScoped<IAdapterEmail, AdapterGraph>();
        servizi.AddScoped<IAdapterEmail, AdapterGmail>();
        servizi.AddScoped<IAdapterEmail, AdapterSendGrid>();
        servizi.TryAddScoped<IInvioEmail, InvioEmailPerTenant>();
        servizi.TryAddScoped<IAccodatoreEmail, AccodatoreEmail>();
        servizi.TryAddScoped<IServizioImpostazioniEmail, ServizioImpostazioniEmail>();
        servizi.AddScoped<IGestoreOutbox, GestoreInvioEmail>();
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

    internal static void ConfiguraSqlServer(SqlServerDbContextOptionsBuilder opzioni)
    {
        opzioni.MigrationsHistoryTable(ContestoDatabase.NomeTabellaStoricoMigrazioni);
    }
}

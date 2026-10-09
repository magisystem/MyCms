using DvCms.Application.Accessi;
using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;
using DvCms.Application.Outbox;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Accessi;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Localizzazione;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.Infrastructure.Piattaforma;
using DvCms.Infrastructure.Popolamento;
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
        servizi.TryAddScoped<ServizioPopolamento>();
        servizi.TryAddScoped<IServizioPiattaforma, ServizioPiattaforma>();
        servizi.TryAddScoped<IServizioPagine, ServizioPagine>();
        servizi.TryAddScoped<IServizioPaginePubbliche, ServizioPaginePubbliche>();
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
        servizi.TryAddSingleton<ElaboratoreOutbox>();
        servizi.AddScoped<IGestoreOutbox, GestorePaginaPubblicata>();
        servizi.AddScoped<IGestoreOutbox, GestorePaginaRitirata>();
        servizi.AddHostedService<ServizioOutbox>();
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

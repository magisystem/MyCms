using DvCms.Application.Accessi;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Accessi;
using DvCms.Infrastructure.Localizzazione;
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

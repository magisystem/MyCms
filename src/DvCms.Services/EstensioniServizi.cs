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
using DvCms.Core.Configurazione;
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
using DvCms.Services.Accessi;
using DvCms.Services.Cache;
using DvCms.Services.Localizzazione;
using DvCms.Services.Manutenzione;
using DvCms.Services.Outbox;
using DvCms.Services.Piattaforma;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DvCms.Services;

public static class EstensioniServizi
{
    /// <summary>
    /// Registra i servizi di accesso, piattaforma, manutenzione, localizzazione, elaborazione dell'outbox e invalidazione della cache.
    /// Presuppone <c>AggiungiData</c> e il progetto del provider del database (ADR-0024).
    /// </summary>
    public static IServiceCollection AggiungiServizi(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        AggiungiAccessi(servizi, configurazione);
        AggiungiLocalizzazione(servizi);
        AggiungiManutenzione(servizi, configurazione);
        AggiungiOutboxECache(servizi);

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
        servizi.TryAddScoped<IServizioAccesso, ServizioAccesso>();
        servizi.TryAddScoped<IServizioDueFattori, ServizioDueFattori>();
        servizi.TryAddSingleton(LeggiOpzioniRecuperoPassword(configurazione));
        servizi.TryAddScoped<IServizioRecuperoPassword, ServizioRecuperoPassword>();
        servizi.TryAddSingleton(new OpzioniInviti(TimeSpan.FromHours(configurazione.LeggiIntero("Sicurezza:Inviti:OreValidita", (int)OpzioniInviti.Predefinite.Validita.TotalHours, 1, 720))));
        servizi.TryAddScoped<IServizioInviti, ServizioInviti>();
        servizi.TryAddSingleton(new OpzioniAcquirenti(
            TimeSpan.FromHours(configurazione.LeggiIntero("Sicurezza:Acquirenti:OreValiditaConferma", (int)OpzioniAcquirenti.Predefinite.ValiditaConferma.TotalHours, 1, 168)),
            configurazione.LeggiIntero("Sicurezza:Acquirenti:RichiesteOrarieMassimeConferma", OpzioniAcquirenti.Predefinite.RichiesteOrarieMassimeConferma, 1, 20),
            TimeSpan.FromDays(configurazione.LeggiIntero("Sicurezza:Acquirenti:GiorniConservazioneNonConfermati", (int)OpzioniAcquirenti.Predefinite.ConservazioneNonConfermati.TotalDays, 1, 90))));
        servizi.TryAddScoped<IServizioAcquirenti, ServizioAcquirenti>();

        servizi.TryAddScoped<IServizioPiattaforma, ServizioPiattaforma>();
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
            TimeSpan.FromMinutes(configurazione.LeggiIntero("Manutenzione:IntervalloMinuti", (int)p.Intervallo.TotalMinutes, 1, 10_080)),
            TimeSpan.FromDays(configurazione.LeggiIntero("Manutenzione:GiorniConservazioneCompletati", (int)p.ConservazioneCompletati.TotalDays, 1, 3650)),
            TimeSpan.FromDays(configurazione.LeggiIntero("Manutenzione:GiorniConservazioneFalliti", (int)p.ConservazioneFalliti.TotalDays, 1, 3650)),
            TimeSpan.FromDays(configurazione.LeggiIntero("Manutenzione:GiorniConservazioneAccessi", (int)p.ConservazioneAccessi.TotalDays, 1, 3650)),
            configurazione.LeggiIntero("Manutenzione:DimensioneLotto", p.DimensioneLotto, 1, 10_000)));
        servizi.TryAddSingleton<ManutenzioneDati>();
        servizi.AddHostedService<ServizioManutenzione>();
    }

    private static void AggiungiOutboxECache(IServiceCollection servizi)
    {
        servizi.TryAddScoped<IGestioneCodaOutbox, ServizioCodaOutbox>();
        servizi.TryAddSingleton<ElaboratoreOutbox>();
        servizi.AddHostedService<ServizioOutbox>();
        servizi.TryAddSingleton<ServizioInvalidazioneCache>();
        servizi.AddHostedService(fornitore => fornitore.GetRequiredService<ServizioInvalidazioneCache>());
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

    private static OpzioniRecuperoPassword LeggiOpzioniRecuperoPassword(IConfiguration configurazione)
    {
        var p = OpzioniRecuperoPassword.Predefinite;
        return new OpzioniRecuperoPassword(
            TimeSpan.FromMinutes(configurazione.LeggiIntero("Sicurezza:RecuperoPassword:MinutiValiditaCodice", (int)p.ValiditaCodice.TotalMinutes, 1, 1440)),
            configurazione.LeggiIntero("Sicurezza:RecuperoPassword:TentativiCodice", p.TentativiCodice, 1, 20),
            TimeSpan.FromMinutes(configurazione.LeggiIntero("Sicurezza:RecuperoPassword:MinutiValiditaLink", (int)p.ValiditaLink.TotalMinutes, 1, 1440)),
            configurazione.LeggiIntero("Sicurezza:RecuperoPassword:RichiesteOrarieAccount", p.RichiesteOrarieAccount, 1, 100),
            configurazione.LeggiIntero("Sicurezza:RecuperoPassword:RichiesteOrarieIp", p.RichiesteOrarieIp, 1, 1000));
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
        var fattore = configurazione.LeggiIntero("Sicurezza:Password:FattoreLavoroBcrypt", OpzioniSicurezza.FattoreLavoroPredefinito, 4, 31);
        var tentativi = configurazione.LeggiIntero("Sicurezza:Accesso:TentativiMassimi", OpzioniSicurezza.TentativiMassimiPredefiniti, 1, 100);
        var minutiBlocco = configurazione.LeggiIntero("Sicurezza:Accesso:MinutiBlocco", OpzioniSicurezza.MinutiBloccoPredefiniti, 1, 1440);
        return new OpzioniSicurezza(fattore, tentativi, TimeSpan.FromMinutes(minutiBlocco));
    }
}

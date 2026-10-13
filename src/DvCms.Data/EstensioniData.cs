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
using DvCms.Data.Accessi;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DvCms.Data;

public static class EstensioniData
{
    /// <summary>
    /// Registra ciò che dipende dal database ma non dal provider: contesto e risolutore del tenant, protezione dei segreti
    /// (tre progetti la usano: accessi, email, pagamenti). Il contesto del database lo registra il progetto del provider.
    /// </summary>
    public static IServiceCollection AggiungiData(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        servizi.TryAddSingleton(TimeProvider.System);

        servizi.TryAddScoped<ContestoTenant>();
        servizi.TryAddScoped<IContestoTenant>(fornitore => fornitore.GetRequiredService<ContestoTenant>());
        servizi.TryAddScoped<IRisolutoreTenant, RisolutoreTenantDatabase>();

        servizi.TryAddSingleton(new ChiaveMaestraSegreti(configurazione[ChiaveMaestraSegreti.NomeConfigurazione]));
        servizi.TryAddSingleton<RepositoryChiaviSegrete>();
        servizi.TryAddSingleton<IProtettoreSegreti>(fornitore => new ProtettoreSegreti(
            fornitore.GetRequiredService<RepositoryChiaviSegrete>(),
            fornitore.GetRequiredService<ChiaveMaestraSegreti>(),
            fornitore.GetRequiredService<TimeProvider>()));

        servizi.TryAddScoped<ServizioPopolamento>();
        servizi.TryAddScoped<ContatoreTentativi>();
        AggiungiCache(servizi, configurazione);
        AggiungiOutbox(servizi, configurazione);

        return servizi;
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
            TimeSpan.FromSeconds(configurazione.LeggiIntero("Cache:SecondiValidita", (int)p.Durata.TotalSeconds, 1, 86_400)),
            configurazione.LeggiIntero("Cache:VociMassime", p.VociMassime, 10, 1_000_000),
            TimeSpan.FromSeconds(configurazione.LeggiIntero("Cache:IntervalloLetturaSecondi", (int)p.IntervalloLettura.TotalSeconds, 1, 600))));
        servizi.TryAddSingleton<ICachePubblica, CachePubblica>();
        servizi.TryAddSingleton<InterceptorInvalidazioneCache>();
        servizi.TryAddScoped<IInvalidazioneCache, ScrittoreInvalidazioneCache>();
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
            TimeSpan.FromSeconds(configurazione.LeggiIntero("Outbox:IntervalloSecondi", (int)predefinite.Intervallo.TotalSeconds, 1, 3600)),
            configurazione.LeggiIntero("Outbox:DimensioneLotto", predefinite.DimensioneLotto, 1, 500),
            configurazione.LeggiIntero("Outbox:TentativiMassimi", predefinite.TentativiMassimi, 1, 50),
            predefinite.DurataPrenotazione);
        servizi.TryAddSingleton(opzioni);
        servizi.TryAddScoped<IOutbox, ScrittoreOutbox>();
    }
}

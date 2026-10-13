using DvCms.Contracts.Email;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Segreti;
using DvCms.Core.Tenancy;
using DvCms.Data.Segreti;
using DvCms.Data.Tenancy;
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

        return servizi;
    }
}

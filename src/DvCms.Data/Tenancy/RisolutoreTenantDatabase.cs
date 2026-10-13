using DvCms.Contracts.Tenancy;
using DvCms.Data;
using DvCms.DTO.Tenancy;
using DvCms.Extender;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DvCms.Data.Tenancy;

/// <summary>
/// Risolve l'host nel tenant leggendo i domini registrati. È l'unico punto, oltre ai casi d'uso privilegiati di
/// piattaforma, che legge dati dei tenant senza il filtro di tenant (non c'è ancora un tenant da cui filtrare).
/// </summary>
public sealed class RisolutoreTenantDatabase : IRisolutoreTenant
{
    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly IHostEnvironment _ambiente;

    public RisolutoreTenantDatabase(IServiceScopeFactory fabbricaAmbiti, IHostEnvironment ambiente)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _ambiente = ambiente;
    }

    public async Task<RisultatoRisoluzioneTenant> RisolviAsync(string? host, CancellationToken annullamento)
    {
        if (!NormalizzatoreHost.TentaNormalizza(host, out var normalizzato))
        {
            return RisultatoRisoluzioneTenant.NonRegistrato;
        }

        await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        var riga = await (
            from dominio in contesto.DominiSito.IgnoreQueryFilters().AsNoTracking()
            join sito in contesto.Siti.IgnoreQueryFilters().AsNoTracking()
                on new { dominio.IdTenant, dominio.IdSito } equals new { sito.IdTenant, IdSito = sito.Id }
            join tenant in contesto.Tenant.AsNoTracking() on dominio.IdTenant equals tenant.Id
            where dominio.Host == normalizzato
            select new
            {
                IdDominio = dominio.Id,
                dominio.IdTenant,
                dominio.IdSito,
                dominio.Host,
                dominio.SoloSviluppo,
                dominio.Attivo,
                dominio.VerificatoIlUtc,
                StatoSito = sito.Stato,
                StatoTenant = tenant.Stato,
                tenant.Primario,
            }).SingleOrDefaultAsync(annullamento);

        if (riga is null
            || !riga.Attivo
            || riga.VerificatoIlUtc is null
            || (riga.SoloSviluppo && !_ambiente.IsDevelopment()))
        {
            return RisultatoRisoluzioneTenant.NonRegistrato;
        }

        if (riga.StatoTenant != StatoTenant.Attivo || riga.StatoSito != StatoSito.Attivo)
        {
            return RisultatoRisoluzioneTenant.NonDisponibile;
        }

        return RisultatoRisoluzioneTenant.Risolto(
            new DatiTenantRisolto(riga.IdTenant, riga.IdSito, riga.IdDominio, riga.Host, riga.Primario));
    }
}

using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Persistenza;
using Microsoft.Extensions.DependencyInjection;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>Dati di prova per i test di integrazione.</summary>
public static class DatiProva
{
    public sealed record TenantCreato(Guid IdTenant, Guid IdSito, Guid IdDominio, string Host);

    public static async Task<TenantCreato> CreaTenantAsync(
        IServiceProvider fornitore,
        string nome,
        string host,
        bool primario = false,
        bool soloSviluppo = false,
        bool verificato = true,
        bool dominioAttivo = true,
        StatoTenant statoTenant = StatoTenant.Attivo,
        StatoSito statoSito = StatoSito.Attivo,
        CancellationToken annullamento = default)
    {
        var orologio = TimeProvider.System;
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        var tenant = Tenant.Crea(nome, primario, orologio);
        tenant.Stato = statoTenant;
        var sito = Sito.Crea(tenant.Id, $"Sito di {nome}", null, orologio);
        sito.Stato = statoSito;
        var dominio = DominioSito.CreaVerificato(tenant.Id, sito.Id, host, soloSviluppo, null, orologio);
        dominio.Attivo = dominioAttivo;
        if (!verificato)
        {
            dominio.VerificatoIlUtc = null;
        }

        contesto.Tenant.Add(tenant);
        contesto.Siti.Add(sito);
        contesto.DominiSito.Add(dominio);
        contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, orologio));
        await contesto.SaveChangesAsync(annullamento);

        return new TenantCreato(tenant.Id, sito.Id, dominio.Id, dominio.Host);
    }
}

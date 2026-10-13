using DvCms.Contracts.Negozio;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Tenancy;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DvCms.Infrastructure.Pagamenti;

/// <summary>
/// Riconciliazione periodica dei pagamenti con il provider, per tutti i siti: cerca i siti con tentativi rimasti aperti oltre la
/// scadenza della prenotazione e, in un ambito per sito con il suo tenant, fa rileggere al servizio dei pagamenti lo stato presso il
/// provider. Non incassa mai: legge soltanto (l'incasso lo avvia il cliente). Idempotente, eseguibile da più istanze insieme.
/// </summary>
public sealed class RiconciliazionePagamenti
{
    private const int SitiPerPassaggio = 50;

    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly TimeProvider _orologio;

    public RiconciliazionePagamenti(IServiceScopeFactory fabbricaAmbiti, TimeProvider orologio)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _orologio = orologio;
    }

    /// <summary>Restituisce quanti tentativi ha concluso (pagati, falliti, chiusi o da rimborsare).</summary>
    public async Task<int> EseguiAsync(CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var fino = adesso - RiconciliazioneEta.Minima;
        var da = adesso - RiconciliazioneEta.Massima;

        List<(Guid IdTenant, Guid IdSito)> siti;
        await using (var ambito = _fabbricaAmbiti.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            siti = [.. (await contesto.TentativiPagamento.IgnoreQueryFilters().AsNoTracking()
                .Where(t => (t.Stato == StatoTentativoPagamento.Creato || t.Stato == StatoTentativoPagamento.InSospeso) && t.CreatoIlUtc < fino && t.CreatoIlUtc > da)
                .Select(t => new { t.IdTenant, t.IdSito })
                .Distinct()
                .Take(SitiPerPassaggio)
                .ToListAsync(annullamento)).Select(x => (x.IdTenant, x.IdSito))];
        }

        var totale = 0;
        foreach (var (idTenant, idSito) in siti)
        {
            annullamento.ThrowIfCancellationRequested();
            await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var dominio = await contesto.DominiSito.IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.IdTenant == idTenant && d.IdSito == idSito && d.Attivo)
                .OrderBy(d => d.SoloSviluppo).ThenBy(d => d.CreatoIlUtc)
                .FirstOrDefaultAsync(annullamento);
            if (dominio is null)
            {
                continue;
            }

            // Il tenant lo decide il sito che si sta riconciliando, non una richiesta: lo si imposta come farebbe la risoluzione dell'host.
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(idTenant, idSito, dominio.Id, dominio.Host, false));
            totale += await ambito.ServiceProvider.GetRequiredService<IServizioPagamenti>().RiconciliaAsync(annullamento);
        }

        return totale;
    }
}

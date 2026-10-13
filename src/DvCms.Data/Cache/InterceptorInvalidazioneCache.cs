using System.Runtime.CompilerServices;
using DvCms.Contracts.Cms;
using DvCms.Models.Cms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DvCms.Data.Cache;

/// <summary>
/// Dopo un salvataggio riuscito che contiene righe di invalidazione svuota subito la cache locale dei siti coinvolti: chi ha
/// appena modificato un contenuto lo vede aggiornato senza aspettare la lettura del registro. (Le altre istanze, o una richiesta
/// che avesse riempito la cache fra il salvataggio e questo punto, sono coperte dalla lettura periodica del registro.)
/// </summary>
public sealed class InterceptorInvalidazioneCache : SaveChangesInterceptor
{
    private readonly ICachePubblica _cache;
    private readonly ConditionalWeakTable<DbContext, List<Guid>> _inAttesa = [];

    public InterceptorInvalidazioneCache(ICachePubblica cache)
    {
        _cache = cache;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Raccogli(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Raccogli(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Applica(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Applica(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Scarta(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Scarta(eventData.Context);
        return Task.CompletedTask;
    }

    private void Raccogli(DbContext? contesto)
    {
        if (contesto is null)
        {
            return;
        }

        var siti = contesto.ChangeTracker.Entries<InvalidazioneCache>().Where(voce => voce.State == EntityState.Added).Select(voce => voce.Entity.IdSito).Distinct().ToList();
        if (siti.Count > 0)
        {
            _inAttesa.AddOrUpdate(contesto, siti);
        }
    }

    private void Applica(DbContext? contesto)
    {
        if (contesto is not null && _inAttesa.TryGetValue(contesto, out var siti))
        {
            _inAttesa.Remove(contesto);
            foreach (var idSito in siti)
            {
                _cache.Invalida(idSito);
            }
        }
    }

    private void Scarta(DbContext? contesto)
    {
        if (contesto is not null)
        {
            _inAttesa.Remove(contesto);
        }
    }
}

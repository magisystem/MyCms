using DvCms.Application.Cms;
using DvCms.Domain.Cms;
using DvCms.Infrastructure.Persistenza;

namespace DvCms.Infrastructure.Cache;

/// <summary>Scrive la riga di invalidazione nel contesto del chiamante, senza salvare (vedi <see cref="IInvalidazioneCache"/>).</summary>
public sealed class ScrittoreInvalidazioneCache : IInvalidazioneCache
{
    private readonly ContestoDatabase _contesto;
    private readonly TimeProvider _orologio;

    public ScrittoreInvalidazioneCache(ContestoDatabase contesto, TimeProvider orologio)
    {
        _contesto = contesto;
        _orologio = orologio;
    }

    public void Registra(Guid idTenant, Guid idSito)
    {
        // Una riga per sito e per salvataggio basta: più registrazioni dello stesso sito nello stesso salvataggio si fondono.
        if (_contesto.InvalidazioniCache.Local.Any(riga => riga.IdSito == idSito && _contesto.Entry(riga).State == Microsoft.EntityFrameworkCore.EntityState.Added))
        {
            return;
        }

        _contesto.InvalidazioniCache.Add(new InvalidazioneCache { IdTenant = idTenant, IdSito = idSito, CreatoIlUtc = _orologio.GetUtcNow().UtcDateTime });
    }
}

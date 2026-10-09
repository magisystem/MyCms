namespace DvCms.Application.Localizzazione;

/// <summary>Persistenza dei testi statici (implementata in Infrastructure).</summary>
public interface IArchivioTestiStatici
{
    Task<IReadOnlyList<InfoLingua>> CaricaLingueAsync(CancellationToken annullamento);

    Task<IReadOnlyList<RigaTestoStatico>> CaricaTuttiAsync(CancellationToken annullamento);

    /// <summary>
    /// Garantisce che per la chiave esistano le righe di tutte le lingue attive: italiano con <c>DaTradurre</c> falso,
    /// le altre con il testo italiano e <c>DaTradurre</c> vero. Sicura rispetto alla concorrenza: le righe già presenti
    /// non si toccano. Restituisce le righe della chiave (create o già presenti).
    /// </summary>
    Task<IReadOnlyList<RigaTestoStatico>> RegistraChiaveAsync(string chiave, CancellationToken annullamento);
}

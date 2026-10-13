using System.Security.Cryptography;
using DvCms.Contracts.Media;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Media;

/// <summary>Esito del confronto fra i metadati del database e gli oggetti dell'archivio.</summary>
public sealed record EsitoVerificaArchivio(
    int RisorseControllate,
    IReadOnlyList<string> OggettiMancanti,
    IReadOnlyList<string> ImprontaDiversa,
    IReadOnlyList<string> OggettiOrfani)
{
    /// <summary>Vero se ogni risorsa ha il suo oggetto con l'impronta giusta. Gli orfani non contano: sono innocui (vedi operations.md).</summary>
    public bool Coerente => OggettiMancanti.Count == 0 && ImprontaDiversa.Count == 0;
}

/// <summary>
/// Confronta <c>RisorseMedia</c> con l'archivio (G-004): dopo un ripristino dice quali file mancano, quali hanno un contenuto
/// diverso da quello registrato e quali oggetti non hanno più una riga. Legge tutte le risorse di tutti i tenant: è uno strumento
/// da operatore (comando <c>verifica-archivio</c>), non una funzione dell'applicazione web. Non scrive mai nulla.
/// </summary>
public sealed class VerificatoreArchivio
{
    private readonly ContestoDatabase _contesto;
    private readonly IArchivioOggetti _archivio;

    public VerificatoreArchivio(ContestoDatabase contesto, IArchivioOggetti archivio)
    {
        _contesto = contesto;
        _archivio = archivio;
    }

    public async Task<EsitoVerificaArchivio> VerificaAsync(CancellationToken annullamento)
    {
        var risorse = await _contesto.RisorseMedia.IgnoreQueryFilters().AsNoTracking()
            .Select(r => new { r.Id, r.ChiaveOggetto, r.HashSha256 })
            .ToListAsync(annullamento);

        var mancanti = new List<string>();
        var diverse = new List<string>();
        foreach (var risorsa in risorse)
        {
            await using var flusso = await _archivio.ApriAsync(risorsa.ChiaveOggetto, annullamento);
            if (flusso is null)
            {
                mancanti.Add(risorsa.ChiaveOggetto);
                continue;
            }

            var impronta = await SHA256.HashDataAsync(flusso, annullamento);
            if (!CryptographicOperations.FixedTimeEquals(impronta, risorsa.HashSha256))
            {
                diverse.Add(risorsa.ChiaveOggetto);
            }
        }

        var note = risorse.Select(r => r.ChiaveOggetto).ToHashSet(StringComparer.Ordinal);
        var orfani = new List<string>();
        await foreach (var chiave in _archivio.ElencaChiaviAsync(annullamento))
        {
            if (!note.Contains(chiave))
            {
                orfani.Add(chiave);
            }
        }

        return new EsitoVerificaArchivio(risorse.Count, mancanti, diverse, orfani);
    }
}

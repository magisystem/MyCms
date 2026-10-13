using System.Security.Cryptography;
using System.Text;
using DvCms.Contracts.Localizzazione;
using DvCms.Data;
using DvCms.DTO.Localizzazione;
using DvCms.Extender;
using DvCms.Models;
using DvCms.Models.Localizzazione;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DvCms.Infrastructure.Localizzazione;

/// <summary>Persistenza dei testi statici su SQL Server (globali, senza filtro di tenant).</summary>
public sealed class ArchivioTestiStatici : IArchivioTestiStatici
{
    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly TimeProvider _orologio;

    public ArchivioTestiStatici(IServiceScopeFactory fabbricaAmbiti, TimeProvider orologio)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _orologio = orologio;
    }

    public static byte[] CalcolaHash(string chiave)
    {
        ArgumentNullException.ThrowIfNull(chiave);
        return SHA256.HashData(Encoding.UTF8.GetBytes(chiave));
    }

    public async Task<IReadOnlyList<InfoLingua>> CaricaLingueAsync(CancellationToken annullamento)
    {
        await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        return await contesto.Lingue.AsNoTracking()
            .Select(lingua => new InfoLingua(lingua.Codice, lingua.Cultura, lingua.Predefinita, lingua.Attiva))
            .ToListAsync(annullamento);
    }

    public async Task<IReadOnlyList<RigaTestoStatico>> CaricaTuttiAsync(CancellationToken annullamento)
    {
        await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        return await (
            from testo in contesto.TestiStatici.AsNoTracking()
            join lingua in contesto.Lingue.AsNoTracking() on testo.IdLingua equals lingua.Id
            select new RigaTestoStatico(lingua.Codice, testo.Chiave, testo.Testo, testo.DaTradurre))
            .ToListAsync(annullamento);
    }

    public async Task<IReadOnlyList<RigaTestoStatico>> RegistraChiaveAsync(string chiave, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(chiave);
        if (chiave.Length == 0 || chiave.Length > TestoStatico.LunghezzaMassimaChiave)
        {
            throw new ArgumentOutOfRangeException(nameof(chiave), "Lunghezza della chiave non valida.");
        }

        var hash = CalcolaHash(chiave);

        await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        var lingueAttive = await contesto.Lingue.AsNoTracking().Where(lingua => lingua.Attiva).ToListAsync(annullamento);
        var presenti = await contesto.TestiStatici.AsNoTracking()
            .Where(testo => testo.HashChiave == hash)
            .Select(testo => testo.IdLingua)
            .ToListAsync(annullamento);

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        foreach (var lingua in lingueAttive.Where(lingua => !presenti.Contains(lingua.Id)))
        {
            contesto.TestiStatici.Add(new TestoStatico
            {
                Id = Identificativi.Nuovo(_orologio),
                IdLingua = lingua.Id,
                Chiave = chiave,
                HashChiave = hash,
                Testo = chiave,
                DaTradurre = lingua.Id != Lingua.IdItaliano,
                AggiornatoIlUtc = adesso,
            });
        }

        try
        {
            await contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateException eccezione) when (contesto.RilevatoreErrori?.EViolazioneUnicita(eccezione) ?? false)
        {
            // Un'altra istanza ha creato le stesse righe nel frattempo: non è un errore, si rilegge sotto.
            contesto.ChangeTracker.Clear();
        }

        return await (
            from testo in contesto.TestiStatici.AsNoTracking()
            join lingua in contesto.Lingue.AsNoTracking() on testo.IdLingua equals lingua.Id
            where testo.HashChiave == hash
            select new RigaTestoStatico(lingua.Codice, testo.Chiave, testo.Testo, testo.DaTradurre))
            .ToListAsync(annullamento);
    }
}

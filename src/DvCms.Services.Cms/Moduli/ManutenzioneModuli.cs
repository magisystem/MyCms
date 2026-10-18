using DvCms.Contracts.Accessi;
using DvCms.Contracts.Manutenzione;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Cms.Moduli;

/// <summary>
/// Fine conservazione delle richieste dei moduli (ADR-0027, D3): ogni modulo conserva le richieste per i giorni che il suo tenant ha scelto
/// (30–1825, predefiniti 365); passato quel tempo si cancellano, a lotti. Nel registro di audit resta solo il numero.
/// </summary>
public sealed class ManutenzioneModuli : IAttivitaManutenzione
{
    private const int Lotto = 500;

    private readonly ContestoDatabase _contesto;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ManutenzioneModuli(ContestoDatabase contesto, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<int> EseguiAsync(CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var moduli = await _contesto.Moduli.IgnoreQueryFilters().AsNoTracking()
            .Where(m => _contesto.InviiModulo.IgnoreQueryFilters().Any(i => i.IdModulo == m.Id))
            .Select(m => new { m.Id, m.IdTenant, m.GiorniConservazione })
            .ToListAsync(annullamento);

        var totale = 0;
        foreach (var modulo in moduli)
        {
            var limite = adesso.AddDays(-modulo.GiorniConservazione);
            var cancellate = 0;
            int lotto;
            do
            {
                annullamento.ThrowIfCancellationRequested();
                lotto = await _contesto.InviiModulo.IgnoreQueryFilters()
                    .Where(i => i.IdModulo == modulo.Id && i.IdTenant == modulo.IdTenant && i.CreatoIlUtc < limite)
                    .Take(Lotto)
                    .ExecuteDeleteAsync(annullamento);
                cancellate += lotto;
            }
            while (lotto >= Lotto);

            if (cancellate > 0)
            {
                totale += cancellate;
                await _audit.RegistraAsync(AzioniAudit.ScadenzaInviiModulo, $"Modulo:{modulo.Id}", RegistroAudit.EsitoRiuscito, modulo.IdTenant, null, $"{cancellate} richieste oltre i {modulo.GiorniConservazione} giorni di conservazione", annullamento);
            }
        }

        return totale;
    }
}

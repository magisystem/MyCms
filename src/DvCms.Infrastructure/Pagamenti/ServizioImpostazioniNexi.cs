using System.Text.Json;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Email;
using DvCms.Contracts.Negozio;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Pagamenti;

/// <summary>Credenziali Nexi per sito (vedi <see cref="IServizioImpostazioniNexi"/>).</summary>
public sealed class ServizioImpostazioniNexi : IServizioImpostazioniNexi
{
    private const string ChiaveSegreto = "ChiaveApi";
    private const int LunghezzaMassimaChiave = 200;

    private readonly ContestoDatabase _contesto;
    private readonly IProtettoreSegreti _protettore;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioImpostazioniNexi(ContestoDatabase contesto, IProtettoreSegreti protettore, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _protettore = protettore;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<SchedaNexiSito?> LeggiAsync(Guid idAttore, Guid idTenant, Guid idSito, CancellationToken annullamento)
    {
        if (!await EPlatformAdminAsync(idAttore, annullamento))
        {
            throw new UnauthorizedAccessException("Operazione riservata al PlatformAdmin.");
        }

        var nome = await _contesto.Siti.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == idSito && s.IdTenant == idTenant).Select(s => s.Nome).SingleOrDefaultAsync(annullamento);
        if (nome is null)
        {
            return null;
        }

        var riga = await _contesto.ImpostazioniNexiSiti.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
        if (riga is null)
        {
            return new SchedaNexiSito(nome, AmbientePagamento.Sandbox, false, null, false);
        }

        var impostata = !string.IsNullOrEmpty(LeggiSegreti(riga.SegretiProtetti).GetValueOrDefault(ChiaveSegreto));
        return new SchedaNexiSito(nome, riga.Ambiente, impostata, Convert.ToBase64String(riga.TokenConcorrenza), impostata);
    }

    public async Task<EsitoOperazione> SalvaAsync(Guid idAttore, Guid idTenant, Guid idSito, AmbientePagamento ambiente, string? chiaveApi, string? tokenConcorrenza, CancellationToken annullamento)
    {
        if (!await EPlatformAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, idTenant, annullamento);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!await _contesto.Siti.IgnoreQueryFilters().AnyAsync(s => s.Id == idSito && s.IdTenant == idTenant, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var riga = await _contesto.ImpostazioniNexiSiti.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
        if (riga is not null && !string.IsNullOrWhiteSpace(tokenConcorrenza))
        {
            try
            {
                _contesto.Entry(riga).Property(i => i.TokenConcorrenza).OriginalValue = Convert.FromBase64String(tokenConcorrenza);
            }
            catch (FormatException)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["TokenConcorrenza"]);
            }
        }

        var segreti = LeggiSegreti(riga?.SegretiProtetti);
        var nuova = chiaveApi?.Trim();
        if (!string.IsNullOrEmpty(nuova))
        {
            segreti[ChiaveSegreto] = nuova;
        }

        var chiave = segreti.GetValueOrDefault(ChiaveSegreto);
        var errori = new List<string>();
        if (!Enum.IsDefined(ambiente))
        {
            errori.Add("Ambiente");
        }

        if (string.IsNullOrEmpty(chiave) || chiave.Length > LunghezzaMassimaChiave || chiave.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            errori.Add(ChiaveSegreto);
        }

        if (errori.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
        }

        string protetti;
        try
        {
            protetti = _protettore.Proteggi(JsonSerializer.Serialize(segreti));
        }
        catch (InvalidOperationException)
        {
            return new EsitoOperazione(CodiceEsito.ChiaveSegretiMancante);
        }

        if (riga is null)
        {
            riga = new ImpostazioniNexiSito { Id = Identificativi.Nuovo(_orologio), IdTenant = idTenant, IdSito = idSito };
            _contesto.ImpostazioniNexiSiti.Add(riga);
        }

        riga.Ambiente = ambiente;
        riga.SegretiProtetti = protetti;
        riga.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        riga.AggiornatoDaIdUtenza = idAttore;

        try
        {
            // Nell'audit solo l'ambiente, mai la chiave.
            await _audit.RegistraAsync(AzioniAudit.ModificaImpostazioniPagamento, $"Sito:{idSito}", RegistroAudit.EsitoRiuscito, idTenant, idAttore, $"Nexi, ambiente {ambiente}", annullamento);
        }
        catch (DbUpdateException)
        {
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, riga.Id);
    }

    /// <summary>Le credenziali complete del sito, o <c>null</c> se non ci sono o non si leggono (chiave dei segreti cambiata).</summary>
    public async Task<CredenzialiNexi?> OttieniAsync(Guid idTenant, Guid idSito, CancellationToken annullamento)
    {
        var riga = await _contesto.ImpostazioniNexiSiti.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
        if (riga is null)
        {
            return null;
        }

        var chiave = LeggiSegreti(riga.SegretiProtetti).GetValueOrDefault(ChiaveSegreto);
        return string.IsNullOrEmpty(chiave) ? null : new CredenzialiNexi(riga.Ambiente, chiave);
    }

    private Dictionary<string, string> LeggiSegreti(string? protetti)
    {
        if (string.IsNullOrEmpty(protetti))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(_protettore.Rivela(protetti)) ?? [], StringComparer.Ordinal);
        }
        catch (InvalidOperationException)
        {
            // Chiave dei segreti cambiata o mancante: il segreto risulta non impostato e va reinserito.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private Task<bool> EPlatformAdminAsync(Guid idAttore, CancellationToken annullamento) =>
        _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAttore && candidata.Ruolo == RuoloUtenza.PlatformAdmin && candidata.Stato == StatoUtenza.Attiva,
            annullamento);

    private async Task RegistraNegazioneAsync(Guid idAttore, Guid idTenant, CancellationToken annullamento)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(c => c.Id == idAttore, annullamento);
        var tenantEsiste = await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(t => t.Id == idTenant, annullamento);
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "ImpostazioniNexiSito", RegistroAudit.EsitoNegato, tenantEsiste ? idTenant : null, attoreEsiste ? idAttore : null, "azione richiesta: ModificaImpostazioniPagamento", annullamento);
    }
}

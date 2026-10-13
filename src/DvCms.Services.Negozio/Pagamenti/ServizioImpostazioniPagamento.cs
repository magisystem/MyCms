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

namespace DvCms.Services.Negozio.Pagamenti;

/// <summary>Credenziali PayPal per sito (vedi <see cref="IServizioImpostazioniPagamento"/>).</summary>
public sealed class ServizioImpostazioniPagamento : IServizioImpostazioniPagamento, IArchivioCredenzialiPagamento
{
    private const string ChiaveSegreto = "SegretoClient";
    private const string ChiaveWebhook = "IdWebhook";

    private readonly ContestoDatabase _contesto;
    private readonly IProtettoreSegreti _protettore;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioImpostazioniPagamento(ContestoDatabase contesto, IProtettoreSegreti protettore, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _protettore = protettore;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<SchedaPagamentoSito?> LeggiAsync(Guid idAttore, Guid idTenant, Guid idSito, CancellationToken annullamento)
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

        var riga = await _contesto.ImpostazioniPagamentoSiti.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
        if (riga is null)
        {
            return new SchedaPagamentoSito(nome, AmbientePagamento.Sandbox, string.Empty, false, false, null, false);
        }

        var segreti = LeggiSegreti(riga.SegretiProtetti);
        var segretoImpostato = !string.IsNullOrEmpty(segreti.GetValueOrDefault(ChiaveSegreto));
        var webhookImpostato = !string.IsNullOrEmpty(segreti.GetValueOrDefault(ChiaveWebhook));
        return new SchedaPagamentoSito(
            nome,
            riga.Ambiente,
            riga.IdClient,
            segretoImpostato,
            webhookImpostato,
            Convert.ToBase64String(riga.TokenConcorrenza),
            !string.IsNullOrEmpty(riga.IdClient) && segretoImpostato && webhookImpostato);
    }

    public async Task<EsitoOperazione> SalvaAsync(
        Guid idAttore,
        Guid idTenant,
        Guid idSito,
        AmbientePagamento ambiente,
        string? idClient,
        string? segretoClient,
        string? idWebhook,
        string? tokenConcorrenza,
        CancellationToken annullamento)
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

        var riga = await _contesto.ImpostazioniPagamentoSiti.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
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
        var nuovoSegreto = segretoClient?.Trim();
        var nuovoWebhook = idWebhook?.Trim();
        if (!string.IsNullOrEmpty(nuovoSegreto))
        {
            segreti[ChiaveSegreto] = nuovoSegreto;
        }

        if (!string.IsNullOrEmpty(nuovoWebhook))
        {
            segreti[ChiaveWebhook] = nuovoWebhook;
        }

        var pulito = idClient?.Trim() ?? string.Empty;
        var errori = new List<string>();
        if (!Enum.IsDefined(ambiente))
        {
            errori.Add("Ambiente");
        }

        if (pulito.Length is 0 or > ImpostazioniPagamentoSito.LunghezzaMassimaIdClient || pulito.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            errori.Add("IdClient");
        }

        if (string.IsNullOrEmpty(segreti.GetValueOrDefault(ChiaveSegreto)) || segreti[ChiaveSegreto].Any(char.IsControl))
        {
            errori.Add(ChiaveSegreto);
        }

        if (string.IsNullOrEmpty(segreti.GetValueOrDefault(ChiaveWebhook)) || segreti[ChiaveWebhook].Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            errori.Add(ChiaveWebhook);
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
            riga = new ImpostazioniPagamentoSito { Id = Identificativi.Nuovo(_orologio), IdTenant = idTenant, IdSito = idSito };
            _contesto.ImpostazioniPagamentoSiti.Add(riga);
        }

        riga.Ambiente = ambiente;
        riga.IdClient = pulito;
        riga.SegretiProtetti = protetti;
        riga.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        riga.AggiornatoDaIdUtenza = idAttore;

        try
        {
            // Nell'audit solo ambiente e identificativo client, mai i segreti.
            await _audit.RegistraAsync(
                AzioniAudit.ModificaImpostazioniPagamento,
                $"Sito:{idSito}",
                RegistroAudit.EsitoRiuscito,
                idTenant,
                idAttore,
                $"ambiente {ambiente}, client {pulito}",
                annullamento);
        }
        catch (DbUpdateException)
        {
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, riga.Id);
    }

    public async Task<CredenzialiPayPal?> OttieniAsync(Guid idTenant, Guid idSito, CancellationToken annullamento)
    {
        var riga = await _contesto.ImpostazioniPagamentoSiti.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
        if (riga is null || string.IsNullOrEmpty(riga.IdClient))
        {
            return null;
        }

        var segreti = LeggiSegreti(riga.SegretiProtetti);
        var segreto = segreti.GetValueOrDefault(ChiaveSegreto);
        var webhook = segreti.GetValueOrDefault(ChiaveWebhook);
        return string.IsNullOrEmpty(segreto) || string.IsNullOrEmpty(webhook) ? null : new CredenzialiPayPal(riga.Ambiente, riga.IdClient, segreto, webhook);
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
            // Chiave dei segreti cambiata o mancante: i segreti risultano non impostati e vanno reinseriti.
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
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "ImpostazioniPagamentoSito",
            RegistroAudit.EsitoNegato,
            tenantEsiste ? idTenant : null,
            attoreEsiste ? idAttore : null,
            "azione richiesta: ModificaImpostazioniPagamento",
            annullamento);
    }
}

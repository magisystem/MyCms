using System.Text.Json;
using DvCms.Application.Accessi;
using DvCms.Application.Email;
using DvCms.Application.Piattaforma;
using DvCms.Domain;
using DvCms.Domain.Accessi;
using DvCms.Domain.Email;
using DvCms.Domain.Outbox;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Email;

public sealed class ServizioImpostazioniEmail : IServizioImpostazioniEmail
{
    private readonly ContestoDatabase _contesto;
    private readonly IProtettoreSegreti _protettore;
    private readonly IAccodatoreEmail _accodatore;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioImpostazioniEmail(
        ContestoDatabase contesto,
        IProtettoreSegreti protettore,
        IAccodatoreEmail accodatore,
        IServizioAudit audit,
        TimeProvider orologio)
    {
        _contesto = contesto;
        _protettore = protettore;
        _accodatore = accodatore;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<SchedaEmailTenant?> LeggiAsync(Guid idAttore, Guid idTenant, CancellationToken annullamento)
    {
        if (!await EPlatformAdminAsync(idAttore, annullamento))
        {
            throw new UnauthorizedAccessException("Operazione riservata al PlatformAdmin.");
        }

        var nome = await _contesto.Tenant.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == idTenant).Select(t => t.Nome).SingleOrDefaultAsync(annullamento);
        if (nome is null)
        {
            return null;
        }

        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        ImpostazioniEmailVisibili? visibili = null;
        if (riga is not null)
        {
            var segreti = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                // L'app OAuth è del TenantAdmin (ADR-0021): il PlatformAdmin vede il canale e nient'altro.
                if (!string.IsNullOrEmpty(riga.SegretiProtetti) && !riga.Provider.UsaOAuth())
                {
                    segreti.UnionWith((JsonSerializer.Deserialize<Dictionary<string, string>>(_protettore.Rivela(riga.SegretiProtetti)) ?? []).Keys);
                }
            }
            catch (InvalidOperationException)
            {
                // Chiave dei segreti cambiata o mancante: si mostra come "non impostato" e il salvataggio chiederà di reinserirli.
            }

            visibili = new ImpostazioniEmailVisibili(
                riga.Provider,
                riga.Provider.UsaOAuth() ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(riga.ValoriJson) ?? [],
                segreti,
                Convert.ToBase64String(riga.TokenConcorrenza),
                riga.AggiornatoIlUtc);
        }

        var problemi = await _contesto.MessaggiOutbox.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IdTenant == idTenant && m.Tipo == TipiEventoEmail.Invio
                && (m.Stato == StatoMessaggioOutbox.Fallito || (m.Stato == StatoMessaggioOutbox.Pendente && m.Tentativi > 0)))
            .OrderByDescending(m => m.CreatoIlUtc)
            .Take(10)
            .Select(m => new ProblemaInvioEmail(m.Id, m.Stato, m.Tentativi, m.UltimoErrore, m.CreatoIlUtc))
            .ToListAsync(annullamento);
        return new SchedaEmailTenant(nome, visibili, problemi);
    }

    public async Task<EsitoSalvataggioEmail> SalvaAsync(
        Guid idAttore,
        Guid idTenant,
        ProviderEmail provider,
        IReadOnlyDictionary<string, string> valori,
        IReadOnlyCollection<string> segretiDaRimuovere,
        string? tokenConcorrenza,
        CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(valori);
        ArgumentNullException.ThrowIfNull(segretiDaRimuovere);

        if (!await EPlatformAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, idTenant, AzioniAudit.ModificaImpostazioniEmail, annullamento);
            return new EsitoSalvataggioEmail(CodiceEsito.NonAutorizzato, []);
        }

        if (!Enum.IsDefined(provider))
        {
            return new EsitoSalvataggioEmail(CodiceEsito.DatiNonValidi, ["Provider"]);
        }

        if (!await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(t => t.Id == idTenant, annullamento))
        {
            return new EsitoSalvataggioEmail(CodiceEsito.NonTrovato, []);
        }

        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        if (provider.UsaOAuth() || riga?.Provider.UsaOAuth() == true)
        {
            // I canali con consenso OAuth (Microsoft 365, Google Mail) li configura e li rimuove solo il TenantAdmin (ADR-0021).
            await RegistraNegazioneAsync(idAttore, idTenant, AzioniAudit.ModificaImpostazioniEmail, annullamento);
            return new EsitoSalvataggioEmail(CodiceEsito.NonConsentito, []);
        }

        if (riga is not null && !string.IsNullOrWhiteSpace(tokenConcorrenza))
        {
            try
            {
                _contesto.Entry(riga).Property(i => i.TokenConcorrenza).OriginalValue = Convert.FromBase64String(tokenConcorrenza);
            }
            catch (FormatException)
            {
                return new EsitoSalvataggioEmail(CodiceEsito.DatiNonValidi, []);
            }
        }

        var campi = DefinizioniProviderEmail.Campi(provider);
        Dictionary<string, string> segretiAttuali;
        try
        {
            segretiAttuali = riga is { SegretiProtetti: { Length: > 0 } protetti } && riga.Provider == provider
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(_protettore.Rivela(protetti)) ?? []
                : [];
        }
        catch (InvalidOperationException)
        {
            // Segreti illeggibili (chiave cambiata): vanno reinseriti.
            segretiAttuali = [];
        }

        var normali = new Dictionary<string, string>(StringComparer.Ordinal);
        var segreti = new Dictionary<string, string>(StringComparer.Ordinal);
        var tutti = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var coppia in valori.Where(c => campi.All(campo => campo.Nome != c.Key)))
        {
            tutti[coppia.Key] = coppia.Value;
        }

        foreach (var campo in campi)
        {
            valori.TryGetValue(campo.Nome, out var inserito);
            inserito = inserito?.Trim();
            if (campo.Segreto)
            {
                if (segretiDaRimuovere.Contains(campo.Nome))
                {
                    continue;
                }

                var valore = !string.IsNullOrEmpty(inserito) ? inserito : segretiAttuali.GetValueOrDefault(campo.Nome);
                if (!string.IsNullOrEmpty(valore))
                {
                    segreti[campo.Nome] = valore;
                    tutti[campo.Nome] = valore;
                }
            }
            else if (!string.IsNullOrEmpty(inserito))
            {
                normali[campo.Nome] = inserito;
                tutti[campo.Nome] = inserito;
            }
        }

        var errori = DefinizioniProviderEmail.Valida(provider, tutti);
        if (errori.Count > 0)
        {
            return new EsitoSalvataggioEmail(CodiceEsito.DatiNonValidi, errori);
        }

        string? protettiNuovi;
        try
        {
            protettiNuovi = segreti.Count == 0 ? null : _protettore.Proteggi(JsonSerializer.Serialize(segreti));
        }
        catch (InvalidOperationException)
        {
            return new EsitoSalvataggioEmail(CodiceEsito.ChiaveSegretiMancante, []);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        if (riga is null)
        {
            riga = new ImpostazioniEmailTenant { Id = Identificativi.Nuovo(_orologio), IdTenant = idTenant };
            _contesto.ImpostazioniEmailTenant.Add(riga);
        }

        riga.Provider = provider;
        riga.ValoriJson = JsonSerializer.Serialize(normali);
        riga.SegretiProtetti = protettiNuovi;
        riga.AggiornatoIlUtc = adesso;
        riga.AggiornatoDaIdUtenza = idAttore;

        try
        {
            // Nell'audit solo canale e nomi dei segreti toccati, mai i valori.
            await _audit.RegistraAsync(
                AzioniAudit.ModificaImpostazioniEmail,
                "ImpostazioniEmailTenant",
                RegistroAudit.EsitoRiuscito,
                idTenant,
                idAttore,
                $"canale {provider}; segreti impostati: {string.Join(", ", segreti.Keys.Order(StringComparer.Ordinal))}",
                annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EsitoSalvataggioEmail(CodiceEsito.Conflitto, []);
        }
        catch (DbUpdateException)
        {
            return new EsitoSalvataggioEmail(CodiceEsito.Conflitto, []);
        }

        return new EsitoSalvataggioEmail(CodiceEsito.Riuscito, []);
    }

    public async Task<EsitoOperazione> AccodaProvaAsync(Guid idAttore, Guid idTenant, string? destinatario, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(messaggio);

        if (!await EPlatformAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, idTenant, AzioniAudit.ProvaInvioEmail, annullamento);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!NormalizzatoreEmail.TentaNormalizza(destinatario, out _))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi);
        }

        if (!await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(t => t.Id == idTenant, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        if (riga is null)
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var id = _accodatore.Accoda(idTenant, messaggio with { Destinatario = destinatario!.Trim() }, riservato: false);
        await _audit.RegistraAsync(
            AzioniAudit.ProvaInvioEmail,
            "ImpostazioniEmailTenant",
            RegistroAudit.EsitoRiuscito,
            idTenant,
            idAttore,
            $"canale {riga.Provider}",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, id);
    }

    private Task<bool> EPlatformAdminAsync(Guid idAttore, CancellationToken annullamento) =>
        _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAttore && candidata.Ruolo == RuoloUtenza.PlatformAdmin && candidata.Stato == StatoUtenza.Attiva,
            annullamento);

    private async Task RegistraNegazioneAsync(Guid idAttore, Guid idTenant, string azione, CancellationToken annullamento)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(c => c.Id == idAttore, annullamento);
        var tenantEsiste = await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(t => t.Id == idTenant, annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "ImpostazioniEmailTenant",
            RegistroAudit.EsitoNegato,
            tenantEsiste ? idTenant : null,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {azione}",
            annullamento);
    }
}

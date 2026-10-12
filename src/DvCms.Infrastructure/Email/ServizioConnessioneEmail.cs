using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DvCms.Application.Accessi;
using DvCms.Application.Email;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain;
using DvCms.Domain.Accessi;
using DvCms.Domain.Email;
using DvCms.Domain.Outbox;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Email;

/// <summary>
/// Canali email con consenso OAuth (ADR-0021). Lavora solo sul tenant dell'host e solo per il suo <c>TenantAdmin</c>: il ruolo si
/// riverifica sul database a ogni operazione e il <c>PlatformAdmin</c> è escluso di proposito. Il segreto del client, il refresh token
/// e il token di accesso restano cifrati e non compaiono nei log, nell'audit né negli errori.
/// </summary>
public sealed class ServizioConnessioneEmail : IServizioConnessioneEmail
{
    private static readonly TimeSpan _durataConsenso = TimeSpan.FromMinutes(10);

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IProtettoreSegreti _protettore;
    private readonly IAccodatoreEmail _accodatore;
    private readonly IServizioAudit _audit;
    private readonly ClienteOAuthEmail _cliente;
    private readonly TimeProvider _orologio;

    public ServizioConnessioneEmail(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IProtettoreSegreti protettore,
        IAccodatoreEmail accodatore,
        IServizioAudit audit,
        ClienteOAuthEmail cliente,
        TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _protettore = protettore;
        _accodatore = accodatore;
        _audit = audit;
        _cliente = cliente;
        _orologio = orologio;
    }

    public async Task<SchedaConnessioneEmail> LeggiAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            throw new UnauthorizedAccessException("Operazione riservata al TenantAdmin del tenant.");
        }

        var idTenant = _tenant.IdTenant;
        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        AppOAuthVisibile? app = null;
        ConnessioneVisibile? visibile = null;
        if (riga is not null && riga.Provider.UsaOAuth())
        {
            var segretoImpostato = false;
            try
            {
                segretoImpostato = !string.IsNullOrEmpty(riga.SegretiProtetti)
                    && (JsonSerializer.Deserialize<Dictionary<string, string>>(_protettore.Rivela(riga.SegretiProtetti)) ?? []).ContainsKey("SegretoClient");
            }
            catch (InvalidOperationException)
            {
                // Chiave dei segreti cambiata o mancante: il segreto risulta non impostato e va reinserito.
            }

            var valori = JsonSerializer.Deserialize<Dictionary<string, string>>(riga.ValoriJson) ?? [];
            app = new AppOAuthVisibile(riga.Provider, valori, segretoImpostato, Convert.ToBase64String(riga.TokenConcorrenza), riga.AggiornatoIlUtc);

            var connessione = await _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(c => c.IdTenant == idTenant, annullamento);
            if (connessione is not null)
            {
                visibile = new ConnessioneVisibile(
                    connessione.Stato,
                    connessione.Casella,
                    connessione.CollegataIlUtc,
                    connessione.UltimoRinnovoIlUtc,
                    connessione.UltimoErrore,
                    connessione.UltimoErroreIlUtc,
                    connessione.Provider == riga.Provider && connessione.ImprontaApp == ClienteOAuthEmail.Impronta(riga.Provider, valori));
            }
        }

        var problemi = await _contesto.MessaggiOutbox.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IdTenant == idTenant && m.Tipo == TipiEventoEmail.Invio
                && (m.Stato == StatoMessaggioOutbox.Fallito || (m.Stato == StatoMessaggioOutbox.Pendente && m.Tentativi > 0)))
            .OrderByDescending(m => m.CreatoIlUtc)
            .Take(5)
            .Select(m => new ProblemaInvioEmail(m.Id, m.Stato, m.Tentativi, m.UltimoErrore, m.CreatoIlUtc))
            .ToListAsync(annullamento);
        return new SchedaConnessioneEmail(riga?.Provider, app, visibile, problemi);
    }

    public async Task<EsitoSalvataggioEmail> SalvaAppAsync(
        Guid idAttore,
        ProviderEmail provider,
        IReadOnlyDictionary<string, string> valori,
        string? tokenConcorrenza,
        bool sostituisciCanale,
        CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(valori);

        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.ModificaAppEmailOAuth);
            return new EsitoSalvataggioEmail(CodiceEsito.NonAutorizzato, []);
        }

        if (!Enum.IsDefined(provider) || !provider.UsaOAuth())
        {
            return new EsitoSalvataggioEmail(CodiceEsito.DatiNonValidi, ["Provider"]);
        }

        var idTenant = _tenant.IdTenant;
        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        if (riga is not null && !riga.Provider.UsaOAuth() && !sostituisciCanale)
        {
            // Il canale configurato dalla piattaforma non si sovrascrive per sbaglio.
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

        Dictionary<string, string> segretiAttuali;
        try
        {
            segretiAttuali = riga is { SegretiProtetti: { Length: > 0 } protetti } && riga.Provider == provider
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(_protettore.Rivela(protetti)) ?? []
                : [];
        }
        catch (InvalidOperationException)
        {
            segretiAttuali = [];
        }

        var campi = DefinizioniProviderEmail.Campi(provider);
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

        string protettiNuovi;
        try
        {
            protettiNuovi = _protettore.Proteggi(JsonSerializer.Serialize(segreti));
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

        // Il consenso vale per un'app precisa: se cambiano canale, tenant Microsoft o client, si deve ricollegare la casella.
        var connessione = await _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.IdTenant == idTenant, annullamento);
        if (connessione is not null && (connessione.Provider != provider || connessione.ImprontaApp != ClienteOAuthEmail.Impronta(provider, tutti)))
        {
            _contesto.ConnessioniEmailOAuth.Remove(connessione);
        }

        return await SalvaConAuditAsync(
            AzioniAudit.ModificaAppEmailOAuth,
            idAttore,
            $"canale {provider}; segreto del client {(segreti.ContainsKey("SegretoClient") ? "impostato" : "assente")}",
            annullamento)
            ? new EsitoSalvataggioEmail(CodiceEsito.Riuscito, [])
            : new EsitoSalvataggioEmail(CodiceEsito.Conflitto, []);
    }

    public async Task<EsitoAvvioCollegamento> AvviaCollegamentoAsync(Guid idAttore, string urlRitorno, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.CollegamentoEmailOAuth);
            return new EsitoAvvioCollegamento(CodiceEsito.NonAutorizzato, null, null);
        }

        if (!Uri.TryCreate(urlRitorno, UriKind.Absolute, out var ritorno) || ritorno.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(ritorno.Fragment))
        {
            return new EsitoAvvioCollegamento(CodiceEsito.DatiNonValidi, null, null);
        }

        var app = await CaricaAppAsync(annullamento);
        if (app is null)
        {
            return new EsitoAvvioCollegamento(CodiceEsito.NonConsentito, null, null);
        }

        if (app.Value.Valori is null)
        {
            return new EsitoAvvioCollegamento(CodiceEsito.ChiaveSegretiMancante, null, null);
        }

        var provider = app.Value.Riga.Provider;
        var verificatore = ClienteOAuthEmail.NuovoVerificatore();
        var stato = ClienteOAuthEmail.NuovoStato();
        string protetto;
        try
        {
            protetto = _protettore.Proteggi(JsonSerializer.Serialize(new StatoCollegamento(
                stato,
                verificatore,
                _tenant.IdTenant,
                idAttore,
                provider,
                ClienteOAuthEmail.Impronta(provider, app.Value.Valori),
                ritorno.AbsoluteUri,
                _orologio.GetUtcNow().UtcDateTime + _durataConsenso)));
        }
        catch (InvalidOperationException)
        {
            return new EsitoAvvioCollegamento(CodiceEsito.ChiaveSegretiMancante, null, null);
        }

        var url = ClienteOAuthEmail.CostruisciUrlAutorizzazione(provider, app.Value.Valori, ritorno.AbsoluteUri, stato, ClienteOAuthEmail.Sfida(verificatore));
        return new EsitoAvvioCollegamento(CodiceEsito.Riuscito, url, protetto);
    }

    public async Task<EsitoCollegamento> CompletaCollegamentoAsync(
        Guid idAttore,
        string urlRitorno,
        string? codice,
        string? stato,
        string? erroreProvider,
        string? statoProtetto,
        CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.CollegamentoEmailOAuth);
            return new EsitoCollegamento(CodiceEsito.NonAutorizzato, null);
        }

        var app = await CaricaAppAsync(annullamento);
        if (app is null)
        {
            return new EsitoCollegamento(CodiceEsito.NonConsentito, null);
        }

        if (app.Value.Valori is null)
        {
            return new EsitoCollegamento(CodiceEsito.ChiaveSegretiMancante, null);
        }

        var richiesta = LeggiStato(statoProtetto);
        var provider = app.Value.Riga.Provider;
        if (richiesta is null
            || string.IsNullOrEmpty(stato)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stato), Encoding.UTF8.GetBytes(richiesta.Stato))
            || richiesta.ScadeIlUtc < _orologio.GetUtcNow().UtcDateTime
            || richiesta.IdTenant != _tenant.IdTenant
            || richiesta.IdAttore != idAttore
            || richiesta.Provider != provider
            || richiesta.Impronta != ClienteOAuthEmail.Impronta(provider, app.Value.Valori)
            || !string.Equals(richiesta.UrlRitorno, urlRitorno, StringComparison.Ordinal))
        {
            await RegistraEsitoAsync(AzioniAudit.CollegamentoEmailOAuth, RegistroAudit.EsitoNegato, idAttore, "stato del consenso non valido o scaduto", annullamento);
            return new EsitoCollegamento(CodiceEsito.ConsensoNegato, null);
        }

        if (!string.IsNullOrEmpty(erroreProvider) || string.IsNullOrWhiteSpace(codice))
        {
            // L'utente ha rifiutato (o il provider ha segnalato un errore): si registra solo che è stato negato.
            await RegistraEsitoAsync(AzioniAudit.CollegamentoEmailOAuth, RegistroAudit.EsitoNegato, idAttore, $"canale {provider}; consenso non concesso", annullamento);
            return new EsitoCollegamento(CodiceEsito.ConsensoNegato, null);
        }

        TokenOAuth token;
        string casella;
        try
        {
            token = await _cliente.ScambiaCodiceAsync(provider, app.Value.Valori, richiesta.UrlRitorno, codice, richiesta.Verificatore, annullamento);
            if (!ClienteOAuthEmail.ConcedeInvio(provider, token.Scope))
            {
                await RegistraEsitoAsync(AzioniAudit.CollegamentoEmailOAuth, RegistroAudit.EsitoNegato, idAttore, $"canale {provider}; permesso di invio non concesso", annullamento);
                return new EsitoCollegamento(CodiceEsito.ConsensoNegato, null);
            }

            if (token.Aggiornamento is null)
            {
                await RegistraEsitoAsync(AzioniAudit.CollegamentoEmailOAuth, RegistroAudit.EsitoNegato, idAttore, $"canale {provider}; il provider non ha emesso un refresh token", annullamento);
                return new EsitoCollegamento(CodiceEsito.ServizioEsternoNonRiuscito, null);
            }

            casella = await _cliente.LeggiCasellaAsync(provider, token.Accesso, annullamento);
        }
        catch (InvioEmailFallitoException eccezione)
        {
            await RegistraEsitoAsync(AzioniAudit.CollegamentoEmailOAuth, RegistroAudit.EsitoNegato, idAttore, $"canale {provider}; {eccezione.Message}", annullamento);
            return new EsitoCollegamento(CodiceEsito.ServizioEsternoNonRiuscito, null);
        }
        catch (ConsensoRevocatoException)
        {
            return new EsitoCollegamento(CodiceEsito.ServizioEsternoNonRiuscito, null);
        }

        var idTenant = _tenant.IdTenant;
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        string aggiornamentoProtetto;
        string accessoProtetto;
        try
        {
            aggiornamentoProtetto = _protettore.Proteggi(token.Aggiornamento);
            accessoProtetto = _protettore.Proteggi(token.Accesso);
        }
        catch (InvalidOperationException)
        {
            return new EsitoCollegamento(CodiceEsito.ChiaveSegretiMancante, null);
        }

        var connessione = await _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.IdTenant == idTenant, annullamento);
        if (connessione is null)
        {
            connessione = new ConnessioneEmailOAuth { Id = Identificativi.Nuovo(_orologio), IdTenant = idTenant };
            _contesto.ConnessioniEmailOAuth.Add(connessione);
        }

        connessione.Provider = provider;
        connessione.ImprontaApp = richiesta.Impronta;
        connessione.Casella = casella;
        connessione.TokenAggiornamentoProtetto = aggiornamentoProtetto;
        connessione.TokenAccessoProtetto = accessoProtetto;
        connessione.ScadenzaAccessoUtc = token.ScadenzaUtc;
        connessione.Stato = StatoConnessioneEmail.Attiva;
        connessione.CollegataIlUtc = adesso;
        connessione.CollegataDaIdUtenza = idAttore;
        connessione.UltimoRinnovoIlUtc = adesso;
        connessione.UltimoErrore = null;
        connessione.UltimoErroreIlUtc = null;

        return await SalvaConAuditAsync(AzioniAudit.CollegamentoEmailOAuth, idAttore, $"canale {provider}; casella {casella}", annullamento)
            ? new EsitoCollegamento(CodiceEsito.Riuscito, casella)
            : new EsitoCollegamento(CodiceEsito.Conflitto, null);
    }

    public async Task<EsitoOperazione> ScollegaAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.ScollegamentoEmailOAuth);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idTenant = _tenant.IdTenant;
        var connessione = await _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.IdTenant == idTenant, annullamento);
        if (connessione is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        await RevocaPressoIlProviderAsync(connessione, annullamento);
        _contesto.ConnessioniEmailOAuth.Remove(connessione);
        return await SalvaConAuditAsync(AzioniAudit.ScollegamentoEmailOAuth, idAttore, $"canale {connessione.Provider}; casella {connessione.Casella}", annullamento)
            ? new EsitoOperazione(CodiceEsito.Riuscito)
            : new EsitoOperazione(CodiceEsito.Conflitto);
    }

    public async Task<EsitoOperazione> RimuoviCanaleAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.ModificaAppEmailOAuth);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idTenant = _tenant.IdTenant;
        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        if (riga is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (!riga.Provider.UsaOAuth())
        {
            // Il canale della piattaforma non è del TenantAdmin.
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var connessione = await _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.IdTenant == idTenant, annullamento);
        if (connessione is not null)
        {
            await RevocaPressoIlProviderAsync(connessione, annullamento);
            _contesto.ConnessioniEmailOAuth.Remove(connessione);
        }

        _contesto.ImpostazioniEmailTenant.Remove(riga);
        return await SalvaConAuditAsync(AzioniAudit.ModificaAppEmailOAuth, idAttore, $"canale {riga.Provider} rimosso", annullamento)
            ? new EsitoOperazione(CodiceEsito.Riuscito)
            : new EsitoOperazione(CodiceEsito.Conflitto);
    }

    public async Task<EsitoOperazione> AccodaProvaAsync(Guid idAttore, string? destinatario, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(messaggio);

        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.ProvaInvioEmail);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!NormalizzatoreEmail.TentaNormalizza(destinatario, out _))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi);
        }

        var idTenant = _tenant.IdTenant;
        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        var attiva = riga is not null && riga.Provider.UsaOAuth()
            && await _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().AsNoTracking().AnyAsync(c => c.IdTenant == idTenant && c.Stato == StatoConnessioneEmail.Attiva, annullamento);
        if (!attiva)
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var id = _accodatore.Accoda(idTenant, messaggio with { Destinatario = destinatario!.Trim() }, riservato: false);
        await _audit.RegistraAsync(AzioniAudit.ProvaInvioEmail, "ImpostazioniEmailTenant", RegistroAudit.EsitoRiuscito, idTenant, idAttore, $"canale {riga!.Provider}", annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, id);
    }

    private async Task RevocaPressoIlProviderAsync(ConnessioneEmailOAuth connessione, CancellationToken annullamento)
    {
        try
        {
            await _cliente.RevocaAsync(connessione.Provider, _protettore.Rivela(connessione.TokenAggiornamentoProtetto), annullamento);
        }
        catch (InvalidOperationException)
        {
            // Chiave dei segreti cambiata: niente da revocare presso il provider, il collegamento locale si elimina comunque.
        }
    }

    /// <summary>Riga e valori completi (segreto compreso); i valori sono nulli se i segreti non si leggono. Nullo se non c'è un'app OAuth.</summary>
    private async Task<(ImpostazioniEmailTenant Riga, Dictionary<string, string>? Valori)?> CaricaAppAsync(CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdTenant == idTenant, annullamento);
        if (riga is null || !riga.Provider.UsaOAuth())
        {
            return null;
        }

        try
        {
            var valori = JsonSerializer.Deserialize<Dictionary<string, string>>(riga.ValoriJson) ?? [];
            foreach (var coppia in JsonSerializer.Deserialize<Dictionary<string, string>>(_protettore.Rivela(riga.SegretiProtetti ?? string.Empty)) ?? [])
            {
                valori[coppia.Key] = coppia.Value;
            }

            return DefinizioniProviderEmail.Valida(riga.Provider, valori).Count == 0 ? (riga, valori) : (riga, null);
        }
        catch (Exception eccezione) when (eccezione is InvalidOperationException or JsonException)
        {
            return (riga, null);
        }
    }

    private StatoCollegamento? LeggiStato(string? statoProtetto)
    {
        if (string.IsNullOrEmpty(statoProtetto))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StatoCollegamento>(_protettore.Rivela(statoProtetto));
        }
        catch (Exception eccezione) when (eccezione is InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    private async Task<bool> SalvaConAuditAsync(string azione, Guid idAttore, string dettagli, CancellationToken annullamento)
    {
        try
        {
            await _audit.RegistraAsync(azione, "ImpostazioniEmailTenant", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, idAttore, dettagli, annullamento);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private Task RegistraEsitoAsync(string azione, string esito, Guid idAttore, string dettagli, CancellationToken annullamento) =>
        _audit.RegistraAsync(azione, "ImpostazioniEmailTenant", esito, _tenant.IdTenant, idAttore, dettagli, annullamento);

    private Task<bool> ETenantAdminAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && candidata.Ruolo == RuoloUtenza.TenantAdmin
                && candidata.IdTenant == idTenant,
            annullamento);
    }

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(c => c.Id == idAttore);
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "ImpostazioniEmailTenant", RegistroAudit.EsitoNegato, _tenant.IdTenant, attoreEsiste ? idAttore : null, $"azione richiesta: {azione}", CancellationToken.None);
    }

    private sealed record StatoCollegamento(string Stato, string Verificatore, Guid IdTenant, Guid IdAttore, ProviderEmail Provider, string Impronta, string UrlRitorno, DateTime ScadeIlUtc);
}

using DvCms.Contracts.Accessi;
using DvCms.Contracts.Accessi.DueFattori;
using DvCms.Contracts.Email;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Accessi.DueFattori;
using DvCms.DTO.Accessi;
using DvCms.DTO.Accessi.DueFattori;
using DvCms.Extender;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Accessi;

/// <summary>
/// Secondo fattore TOTP e codici di recupero (ADR-0019). Come <see cref="ServizioAccesso"/>, legge <c>Utenze</c> senza il filtro
/// globale ma verifica sempre account attivo, ruolo amministrativo e ammissione al tenant dell'host. Contatori e passi usati si
/// aggiornano con istruzioni atomiche sul database, perché richieste parallele non possano riusare un codice.
/// </summary>
public sealed partial class ServizioDueFattori : IServizioDueFattori
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IHashPassword _hash;
    private readonly HashFinto _hashFinto;
    private readonly ContatoreTentativi _contatore;
    private readonly IProtettoreSegreti _segreti;
    private readonly IServizioAudit _audit;
    private readonly IServizioAccesso _accesso;
    private readonly OpzioniDueFattori _opzioni;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ServizioDueFattori> _logger;

    public ServizioDueFattori(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IHashPassword hash,
        HashFinto hashFinto,
        ContatoreTentativi contatore,
        IProtettoreSegreti segreti,
        IServizioAudit audit,
        IServizioAccesso accesso,
        OpzioniDueFattori opzioni,
        TimeProvider orologio,
        ILogger<ServizioDueFattori> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _hash = hash;
        _hashFinto = hashFinto;
        _contatore = contatore;
        _segreti = segreti;
        _audit = audit;
        _accesso = accesso;
        _opzioni = opzioni;
        _orologio = orologio;
        _logger = logger;
    }

    private enum TipoCodice
    {
        Nessuno = 0,
        Totp = 1,
        Recupero = 2,
    }

    public async Task<RisultatoAccesso> CompletaAccessoAsync(Guid idUtenza, Guid timbroSicurezza, string? codice, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var utenza = await CaricaAsync(idUtenza, annullamento);
        if (utenza is null
            || !utenza.DueFattoriAttivi
            || utenza.TimbroSicurezza != timbroSicurezza
            || (utenza.BloccatoFinoAlUtc is { } finoA && finoA > adesso))
        {
            ConsumaTempoDiVerifica();
            return RisultatoAccesso.Fallito;
        }

        var tipo = await VerificaCodiceAsync(utenza, codice, consentiRecupero: true, annullamento);
        if (tipo == TipoCodice.Nessuno)
        {
            await _contatore.RegistraFallimentoAsync(utenza.Id, adesso, annullamento);
            return RisultatoAccesso.Fallito;
        }

        await _contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza).ExecuteUpdateAsync(
            aggiornamento => aggiornamento
                .SetProperty(u => u.ContatoreAccessiFalliti, 0)
                .SetProperty(u => u.BloccatoFinoAlUtc, (DateTime?)null)
                .SetProperty(u => u.UltimoAccessoIlUtc, adesso)
                .SetProperty(u => u.AggiornatoIlUtc, adesso),
            annullamento);

        if (tipo == TipoCodice.Recupero)
        {
            var residui = await _contesto.CodiciRecuperoUtenza.CountAsync(c => c.IdUtenza == idUtenza && c.UsatoIlUtc == null, annullamento);
            await _audit.RegistraAsync(
                AzioniAudit.UsoCodiceRecupero,
                $"Utenza:{idUtenza}",
                RegistroAudit.EsitoRiuscito,
                utenza.IdTenant ?? _tenant.IdTenantOpzionale,
                idUtenza,
                $"codici residui: {residui}",
                annullamento);
        }

        if (utenza.Ruolo == RuoloUtenza.PlatformAdmin)
        {
            await _audit.RegistraAsync(
                AzioniAudit.AccessoPlatformAdmin,
                $"Tenant:{_tenant.IdTenant}",
                RegistroAudit.EsitoRiuscito,
                _tenant.IdTenant,
                utenza.Id,
                dettagli: null,
                annullamento);
        }

        var identita = await _accesso.ConvalidaSessioneAsync(idUtenza, timbroSicurezza, annullamento);
        return identita is null ? RisultatoAccesso.Fallito : RisultatoAccesso.Riuscito(identita);
    }

    public async Task<StatoDueFattori?> LeggiStatoAsync(Guid idUtenza, CancellationToken annullamento)
    {
        var utenza = await CaricaAsync(idUtenza, annullamento);
        if (utenza is null)
        {
            return null;
        }

        var residui = utenza.DueFattoriAttivi
            ? await _contesto.CodiciRecuperoUtenza.CountAsync(c => c.IdUtenza == idUtenza && c.UsatoIlUtc == null, annullamento)
            : 0;
        return new StatoDueFattori(
            utenza.DueFattoriAttivi,
            _opzioni.RichiestaPer(utenza.Ruolo),
            utenza.SegretoTotpInAttesaProtetto is not null,
            residui);
    }

    public async Task<RisultatoDueFattori<ConfigurazioneDueFattori>> AvviaConfigurazioneAsync(
        Guid idUtenza,
        string? password,
        string? codice,
        CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var utenza = await CaricaAsync(idUtenza, annullamento);
        if (utenza is null)
        {
            return new(EsitoDueFattori.NonValido);
        }

        if (utenza.DueFattoriAttivi)
        {
            var esito = await VerificaPasswordECodiceAsync(utenza, password, codice, adesso, annullamento);
            if (esito != EsitoDueFattori.Riuscito)
            {
                return new(esito);
            }
        }

        var segreto = Totp.GeneraSegreto();
        var chiave = Base32.Codifica(segreto);
        string protetto;
        try
        {
            protetto = _segreti.Proteggi(chiave);
        }
        catch (InvalidOperationException)
        {
            RegistraChiaveSegretiMancante(_logger);
            return new(EsitoDueFattori.ChiaveSegretiMancante);
        }

        await _contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza).ExecuteUpdateAsync(
            aggiornamento => aggiornamento
                .SetProperty(u => u.SegretoTotpInAttesaProtetto, protetto)
                .SetProperty(u => u.AggiornatoIlUtc, adesso),
            annullamento);

        return new(EsitoDueFattori.Riuscito, new ConfigurazioneDueFattori(Totp.CreaUri(segreto, _opzioni.Emittente, utenza.Email), RaggruppaChiave(chiave)));
    }

    public async Task<RisultatoDueFattori<AttivazioneDueFattori>> ConfermaAttivazioneAsync(Guid idUtenza, string? codice, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var utenza = await CaricaAsync(idUtenza, annullamento);
        if (utenza is null)
        {
            return new(EsitoDueFattori.NonValido);
        }

        if (utenza.SegretoTotpInAttesaProtetto is not { } inAttesa)
        {
            return new(EsitoDueFattori.NessunaConfigurazione);
        }

        if (utenza.BloccatoFinoAlUtc is { } finoA && finoA > adesso)
        {
            return new(EsitoDueFattori.NonValido);
        }

        long? passo = null;
        if (TentaRivela(inAttesa, out var segreto))
        {
            passo = Totp.Verifica(segreto, codice, new DateTimeOffset(adesso, TimeSpan.Zero));
        }

        if (passo is not { } passoValido)
        {
            await _contatore.RegistraFallimentoAsync(idUtenza, adesso, annullamento);
            return new(EsitoDueFattori.NonValido);
        }

        var nuovoTimbro = Guid.NewGuid();
        var codici = CodiciRecupero.Genera();
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);

        var aggiornate = await _contesto.Utenze.IgnoreQueryFilters()
            .Where(u => u.Id == idUtenza && u.SegretoTotpInAttesaProtetto == inAttesa)
            .ExecuteUpdateAsync(
                aggiornamento => aggiornamento
                    .SetProperty(u => u.SegretoTotpProtetto, inAttesa)
                    .SetProperty(u => u.SegretoTotpInAttesaProtetto, (string?)null)
                    .SetProperty(u => u.DueFattoriAttivi, true)
                    .SetProperty(u => u.DueFattoriAttivatiIlUtc, adesso)
                    .SetProperty(u => u.UltimoPassoTotp, passoValido)
                    .SetProperty(u => u.TimbroSicurezza, nuovoTimbro)
                    .SetProperty(u => u.ContatoreAccessiFalliti, 0)
                    .SetProperty(u => u.AggiornatoIlUtc, adesso),
                annullamento);
        if (aggiornate != 1)
        {
            return new(EsitoDueFattori.NonValido);
        }

        await SostituisciCodiciAsync(idUtenza, codici, adesso, annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.AttivazioneDueFattori,
            $"Utenza:{idUtenza}",
            RegistroAudit.EsitoRiuscito,
            utenza.IdTenant ?? _tenant.IdTenantOpzionale,
            idUtenza,
            utenza.DueFattoriAttivi ? "cambio di dispositivo" : null,
            annullamento);
        await transazione.CommitAsync(annullamento);

        var identita = await _accesso.ConvalidaSessioneAsync(idUtenza, nuovoTimbro, annullamento);
        return identita is null
            ? new(EsitoDueFattori.NonValido)
            : new(EsitoDueFattori.Riuscito, new AttivazioneDueFattori(identita, codici));
    }

    public async Task<RisultatoDueFattori<RigenerazioneCodiciRecupero>> RigeneraCodiciRecuperoAsync(
        Guid idUtenza,
        string? password,
        string? codice,
        CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var utenza = await CaricaAsync(idUtenza, annullamento);
        if (utenza is null || !utenza.DueFattoriAttivi)
        {
            return new(EsitoDueFattori.NonValido);
        }

        var esito = await VerificaPasswordECodiceAsync(utenza, password, codice, adesso, annullamento);
        if (esito != EsitoDueFattori.Riuscito)
        {
            return new(esito);
        }

        var codici = CodiciRecupero.Genera();
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        await SostituisciCodiciAsync(idUtenza, codici, adesso, annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.RigenerazioneCodiciRecupero,
            $"Utenza:{idUtenza}",
            RegistroAudit.EsitoRiuscito,
            utenza.IdTenant ?? _tenant.IdTenantOpzionale,
            idUtenza,
            dettagli: null,
            annullamento);
        await transazione.CommitAsync(annullamento);

        return new(EsitoDueFattori.Riuscito, new RigenerazioneCodiciRecupero(codici));
    }

    public async Task<EsitoDueFattori> DisattivaAsync(Guid idUtenza, string? password, string? codice, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var utenza = await CaricaAsync(idUtenza, annullamento);
        if (utenza is null || !utenza.DueFattoriAttivi)
        {
            return EsitoDueFattori.NonValido;
        }

        if (_opzioni.RichiestaPer(utenza.Ruolo))
        {
            return EsitoDueFattori.NonConsentito;
        }

        var esito = await VerificaPasswordECodiceAsync(utenza, password, codice, adesso, annullamento);
        if (esito != EsitoDueFattori.Riuscito)
        {
            return esito;
        }

        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        var tracciata = await _contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == idUtenza, annullamento);
        await AzzeramentoDueFattori.ApplicaAsync(_contesto, tracciata, adesso, annullamento);
        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.DisattivazioneDueFattori,
            $"Utenza:{idUtenza}",
            RegistroAudit.EsitoRiuscito,
            utenza.IdTenant ?? _tenant.IdTenantOpzionale,
            idUtenza,
            dettagli: null,
            annullamento);
        await transazione.CommitAsync(annullamento);
        return EsitoDueFattori.Riuscito;
    }

    public async Task<bool> ReimpostaPlatformAdminAsync(string? email, CancellationToken annullamento)
    {
        if (!NormalizzatoreEmail.TentaNormalizza(email, out var normalizzata))
        {
            return false;
        }

        var utenza = await _contesto.Utenze.IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.Ruolo == RuoloUtenza.PlatformAdmin && u.IdTenant == null && u.EmailNormalizzata == normalizzata, annullamento);
        if (utenza is null)
        {
            return false;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        await AzzeramentoDueFattori.ApplicaAsync(_contesto, utenza, adesso, annullamento);
        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.ReimpostazioneDueFattori,
            $"Utenza:{utenza.Id}",
            RegistroAudit.EsitoRiuscito,
            idTenant: null,
            idUtenzaAttore: null,
            "comando operativo",
            annullamento);
        await transazione.CommitAsync(annullamento);
        return true;
    }

    private static string RaggruppaChiave(string chiave) =>
        string.Join(' ', Enumerable.Range(0, (chiave.Length + 3) / 4).Select(i => chiave.Substring(i * 4, Math.Min(4, chiave.Length - (i * 4)))));

    private async Task<Utenza?> CaricaAsync(Guid idUtenza, CancellationToken annullamento)
    {
        if (!_tenant.Risolto)
        {
            return null;
        }

        var utenza = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(candidata => candidata.Id == idUtenza, annullamento);
        if (utenza is null
            || utenza.Stato != StatoUtenza.Attiva
            || utenza.Ruolo == RuoloUtenza.Utente
            || (utenza.Ruolo != RuoloUtenza.PlatformAdmin && utenza.IdTenant != _tenant.IdTenant))
        {
            return null;
        }

        return utenza;
    }

    /// <summary>Per le operazioni sensibili con la 2FA attiva: servono sia la password sia un codice (TOTP o di recupero).</summary>
    private async Task<EsitoDueFattori> VerificaPasswordECodiceAsync(
        Utenza utenza,
        string? password,
        string? codice,
        DateTime adesso,
        CancellationToken annullamento)
    {
        if (string.IsNullOrEmpty(password) && string.IsNullOrWhiteSpace(codice))
        {
            return EsitoDueFattori.RichiedeVerifica;
        }

        if (utenza.BloccatoFinoAlUtc is { } finoA && finoA > adesso)
        {
            ConsumaTempoDiVerifica();
            return EsitoDueFattori.NonValido;
        }

        if (string.IsNullOrEmpty(password)
            || password.Length > PoliticaPassword.LunghezzaMassima
            || !_hash.Verifica(password, utenza.HashPassword))
        {
            await _contatore.RegistraFallimentoAsync(utenza.Id, adesso, annullamento);
            return EsitoDueFattori.NonValido;
        }

        if (await VerificaCodiceAsync(utenza, codice, consentiRecupero: true, annullamento) == TipoCodice.Nessuno)
        {
            await _contatore.RegistraFallimentoAsync(utenza.Id, adesso, annullamento);
            return EsitoDueFattori.NonValido;
        }

        return EsitoDueFattori.Riuscito;
    }

    /// <summary>
    /// Verifica un codice TOTP (consumando il passo con un aggiornamento atomico) o, se consentito, un codice di recupero
    /// (consumandolo con un aggiornamento atomico). Un codice già usato non vale più, nemmeno con richieste concorrenti.
    /// </summary>
    private async Task<TipoCodice> VerificaCodiceAsync(Utenza utenza, string? codice, bool consentiRecupero, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow();
        var idUtenza = utenza.Id;

        if (Totp.Normalizza(codice) is not null)
        {
            if (utenza.SegretoTotpProtetto is not { } protetto || !TentaRivela(protetto, out var segreto))
            {
                return TipoCodice.Nessuno;
            }

            if (Totp.Verifica(segreto, codice, adesso) is not { } passo)
            {
                return TipoCodice.Nessuno;
            }

            var consumati = await _contesto.Utenze.IgnoreQueryFilters()
                .Where(u => u.Id == idUtenza && u.UltimoPassoTotp < passo)
                .ExecuteUpdateAsync(aggiornamento => aggiornamento.SetProperty(u => u.UltimoPassoTotp, passo), annullamento);
            return consumati == 1 ? TipoCodice.Totp : TipoCodice.Nessuno;
        }

        if (consentiRecupero && CodiciRecupero.Normalizza(codice) is { } normalizzato)
        {
            var hash = CodiciRecupero.Hash(normalizzato);
            var istante = adesso.UtcDateTime;
            var usati = await _contesto.CodiciRecuperoUtenza
                .Where(c => c.IdUtenza == idUtenza && c.HashCodice == hash && c.UsatoIlUtc == null)
                .ExecuteUpdateAsync(aggiornamento => aggiornamento.SetProperty(c => c.UsatoIlUtc, istante), annullamento);
            return usati == 1 ? TipoCodice.Recupero : TipoCodice.Nessuno;
        }

        ConsumaTempoDiVerifica();
        return TipoCodice.Nessuno;
    }

    private async Task SostituisciCodiciAsync(Guid idUtenza, IReadOnlyList<string> codici, DateTime adesso, CancellationToken annullamento)
    {
        await _contesto.CodiciRecuperoUtenza.Where(c => c.IdUtenza == idUtenza).ExecuteDeleteAsync(annullamento);
        foreach (var codice in codici)
        {
            _contesto.CodiciRecuperoUtenza.Add(new CodiceRecuperoUtenza
            {
                Id = Identificativi.Nuovo(_orologio),
                IdUtenza = idUtenza,
                HashCodice = CodiciRecupero.Hash(CodiciRecupero.Normalizza(codice)!),
                CreatoIlUtc = adesso,
            });
        }

        await _contesto.SaveChangesAsync(annullamento);
    }

    private bool TentaRivela(string protetto, out byte[] segreto)
    {
        segreto = [];
        try
        {
            return Base32.TentaDecodifica(_segreti.Rivela(protetto), out segreto);
        }
        catch (InvalidOperationException)
        {
            // Chiave dei segreti cambiata o dato alterato: la 2FA di questo account non è verificabile, serve una reimpostazione.
            RegistraSegretoIllegibile(_logger);
            return false;
        }
    }

    private void ConsumaTempoDiVerifica() => _hash.Verifica("password-inesistente", _hashFinto.Valore);

    [LoggerMessage(Level = LogLevel.Error, Message = "Impossibile cifrare il segreto TOTP: 'Sicurezza:ChiaveSegreti' non è configurata o non è valida")]
    private static partial void RegistraChiaveSegretiMancante(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Segreto TOTP non leggibile (chiave dei segreti diversa o dato alterato): l'account va reimpostato")]
    private static partial void RegistraSegretoIllegibile(ILogger logger);
}

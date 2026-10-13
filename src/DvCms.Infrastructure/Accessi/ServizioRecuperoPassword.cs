using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Email;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Accessi;
using DvCms.DTO.Email;
using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Localizzazione;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Accessi;

/// <summary>
/// Recupero password in due email (ADR-0011): prima un codice OTP, poi un link monouso. Vedi <see cref="IServizioRecuperoPassword"/>.
/// Come per l'accesso, legge <c>Utenze</c> senza il filtro globale ma sempre con il tenant dell'host nella query.
/// Il tempo di risposta di richieste reali e non reali non è identico al microsecondo, ma il lavoro svolto è lo stesso
/// (una scrittura, la generazione del codice, l'hash) e l'invio non avviene mai nella richiesta: lo fa il worker.
/// </summary>
public sealed partial class ServizioRecuperoPassword : IServizioRecuperoPassword
{
    private static readonly TimeSpan _finestraLimiti = TimeSpan.FromHours(1);

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IHashPassword _hash;
    private readonly IAccodatoreEmail _email;
    private readonly ILocalizzatore _localizzatore;
    private readonly IServizioAudit _audit;
    private readonly OpzioniRecuperoPassword _opzioni;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ServizioRecuperoPassword> _logger;

    public ServizioRecuperoPassword(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IHashPassword hash,
        IAccodatoreEmail email,
        ILocalizzatore localizzatore,
        IServizioAudit audit,
        OpzioniRecuperoPassword opzioni,
        TimeProvider orologio,
        ILogger<ServizioRecuperoPassword> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _hash = hash;
        _email = email;
        _localizzatore = localizzatore;
        _audit = audit;
        _opzioni = opzioni;
        _orologio = orologio;
        _logger = logger;
    }

    public async Task<string> RichiediAsync(string? email, string? indirizzoIp, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var idTenant = _tenant.IdTenant;
        var emailValida = NormalizzatoreEmail.TentaNormalizza(email, out var normalizzata);

        var riferimento = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codice = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var sale = RandomNumberGenerator.GetBytes(16);
        var da = adesso - _finestraLimiti;

        var perAccount = await _contesto.RichiesteRecuperoPassword.IgnoreQueryFilters()
            .CountAsync(r => r.IdTenant == idTenant && r.EmailNormalizzata == normalizzata && r.CreatoIlUtc > da, annullamento);
        var perIp = string.IsNullOrEmpty(indirizzoIp)
            ? 0
            : await _contesto.RichiesteRecuperoPassword.IgnoreQueryFilters().CountAsync(r => r.IndirizzoIp == indirizzoIp && r.CreatoIlUtc > da, annullamento);
        var oltreILimiti = perAccount >= _opzioni.RichiesteOrarieAccount || perIp >= _opzioni.RichiesteOrarieIp;

        // Solo TenantAdmin e Utente del tenant dell'host, attivi: mai il PlatformAdmin.
        var utenza = emailValida && !oltreILimiti
            ? await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(
                candidata => candidata.IdTenant == idTenant
                    && candidata.EmailNormalizzata == normalizzata
                    && candidata.Ruolo != RuoloUtenza.PlatformAdmin
                    && candidata.Stato == StatoUtenza.Attiva,
                annullamento)
            : null;

        var richiesta = new RichiestaRecuperoPassword
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = idTenant,
            EmailNormalizzata = emailValida ? normalizzata : string.Empty,
            IdUtenza = utenza?.Id,
            IndirizzoIp = indirizzoIp is { Length: > 45 } ? indirizzoIp[..45] : indirizzoIp,
            Stato = utenza is null ? StatoRecuperoPassword.Ignorata : StatoRecuperoPassword.InAttesaCodice,
            HashRiferimento = Sha256(riferimento),
            SaleCodice = sale,
            HashCodice = HashCodice(sale, codice),
            ScadenzaCodiceIlUtc = adesso + _opzioni.ValiditaCodice,
            CreatoIlUtc = adesso,
        };

        if (utenza is not null)
        {
            // Una nuova richiesta invalida quelle ancora aperte dello stesso account (codici e link).
            var aperte = await _contesto.RichiesteRecuperoPassword.IgnoreQueryFilters()
                .Where(r => r.IdUtenza == utenza.Id && (r.Stato == StatoRecuperoPassword.InAttesaCodice || r.Stato == StatoRecuperoPassword.LinkInviato))
                .ToListAsync(annullamento);
            foreach (var vecchia in aperte)
            {
                vecchia.Stato = StatoRecuperoPassword.Annullata;
                vecchia.HashToken = null;
            }

            try
            {
                var lingua = await CodiceLinguaAsync(utenza, annullamento);
                _email.Accoda(
                    idTenant,
                    new MessaggioEmail(
                        utenza.Email,
                        utenza.NomeVisualizzato,
                        _localizzatore.TraduciIn(lingua, "Codice per reimpostare la password"),
                        _localizzatore.TraduciIn(
                            lingua,
                            "Il tuo codice di verifica è {0}. Vale {1} minuti. Se non hai chiesto tu di reimpostare la password, ignora questo messaggio.",
                            codice,
                            (int)_opzioni.ValiditaCodice.TotalMinutes)),
                    chiaveIdempotenza: $"recupero-codice:{richiesta.Id}",
                    riservato: true);
            }
            catch (Exception eccezione) when (eccezione is InvalidOperationException or CryptographicException)
            {
                // Ad esempio chiave dei segreti mancante: la richiesta non deve rivelare il problema, lo si vede nei log.
                RegistraErroreAccodamento(_logger, eccezione.GetType().Name);
            }
        }

        _contesto.RichiesteRecuperoPassword.Add(richiesta);
        await _contesto.SaveChangesAsync(annullamento);
        return riferimento;
    }

    public async Task<EsitoVerificaCodice> VerificaCodiceAsync(string? riferimento, string? codice, string urlBase, CancellationToken annullamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlBase);

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var idTenant = _tenant.IdTenant;
        var hashRiferimento = Sha256(riferimento is { Length: > 0 and <= 100 } ? riferimento : "-");

        var richiesta = await _contesto.RichiesteRecuperoPassword.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.HashRiferimento == hashRiferimento && r.IdTenant == idTenant, annullamento);
        if (richiesta is null
            || (richiesta.Stato != StatoRecuperoPassword.InAttesaCodice && richiesta.Stato != StatoRecuperoPassword.Ignorata)
            || richiesta.ScadenzaCodiceIlUtc <= adesso
            || richiesta.TentativiCodice >= _opzioni.TentativiCodice)
        {
            return EsitoVerificaCodice.NonValido;
        }

        var cifre = codice?.Trim() ?? string.Empty;
        var formatoValido = cifre.Length == 6 && cifre.All(char.IsAsciiDigit);
        var corretto = CryptographicOperations.FixedTimeEquals(HashCodice(richiesta.SaleCodice, formatoValido ? cifre : "------"), richiesta.HashCodice)
            && formatoValido
            && richiesta.Stato == StatoRecuperoPassword.InAttesaCodice;

        var utenza = corretto && richiesta.IdUtenza is { } idUtenza
            ? await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(
                u => u.Id == idUtenza && u.IdTenant == idTenant && u.Stato == StatoUtenza.Attiva && u.Ruolo != RuoloUtenza.PlatformAdmin,
                annullamento)
            : null;

        if (utenza is null)
        {
            richiesta.TentativiCodice++;
            if (richiesta.TentativiCodice >= _opzioni.TentativiCodice && richiesta.Stato == StatoRecuperoPassword.InAttesaCodice)
            {
                richiesta.Stato = StatoRecuperoPassword.Annullata;
            }

            await _contesto.SaveChangesAsync(annullamento);
            return EsitoVerificaCodice.NonValido;
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        richiesta.Stato = StatoRecuperoPassword.LinkInviato;
        richiesta.HashToken = Sha256(token);
        richiesta.ScadenzaTokenIlUtc = adesso + _opzioni.ValiditaLink;

        var lingua = await CodiceLinguaAsync(utenza, annullamento);
        var link = $"{urlBase.TrimEnd('/')}/admin/recupero/reimposta?token={token}{(utenza.Ruolo == RuoloUtenza.Utente ? "&da=account" : string.Empty)}";
        _email.Accoda(
            idTenant,
            new MessaggioEmail(
                utenza.Email,
                utenza.NomeVisualizzato,
                _localizzatore.TraduciIn(lingua, "Link per reimpostare la password"),
                _localizzatore.TraduciIn(
                    lingua,
                    "Per scegliere una nuova password apri questo link entro {1} minuti: {0}. Il link si può usare una sola volta. Se non sei stato tu, ignora questo messaggio.",
                    link,
                    (int)_opzioni.ValiditaLink.TotalMinutes)),
            chiaveIdempotenza: $"recupero-link:{richiesta.Id}",
            riservato: true);
        await _contesto.SaveChangesAsync(annullamento);
        return EsitoVerificaCodice.Accettato;
    }

    public async Task<bool> LinkValidoAsync(string? token, CancellationToken annullamento)
    {
        var (richiesta, utenza) = await TrovaPerTokenAsync(token, annullamento);
        return richiesta is not null && utenza is not null;
    }

    public async Task<EsitoReimpostazione> ReimpostaAsync(string? token, string? nuovaPassword, CancellationToken annullamento)
    {
        var (richiesta, utenza) = await TrovaPerTokenAsync(token, annullamento);
        if (richiesta is null || utenza is null)
        {
            return EsitoReimpostazione.LinkNonValido;
        }

        if (PoliticaPassword.Valida(nuovaPassword) is not null)
        {
            // Il link resta valido: l'utente può riprovare con una password accettabile.
            return EsitoReimpostazione.PasswordNonValida;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        utenza.HashPassword = _hash.Calcola(nuovaPassword!);
        utenza.PasswordAggiornataIlUtc = adesso;
        utenza.DeveCambiarePassword = false;
        utenza.TimbroSicurezza = Guid.NewGuid();
        utenza.ContatoreAccessiFalliti = 0;
        utenza.BloccatoFinoAlUtc = null;
        utenza.AggiornatoIlUtc = adesso;

        richiesta.Stato = StatoRecuperoPassword.Completata;
        richiesta.CompletatoIlUtc = adesso;
        richiesta.HashToken = null;
        var altre = await _contesto.RichiesteRecuperoPassword.IgnoreQueryFilters()
            .Where(r => r.IdUtenza == utenza.Id && r.Id != richiesta.Id
                && (r.Stato == StatoRecuperoPassword.InAttesaCodice || r.Stato == StatoRecuperoPassword.LinkInviato))
            .ToListAsync(annullamento);
        foreach (var altra in altre)
        {
            altra.Stato = StatoRecuperoPassword.Annullata;
            altra.HashToken = null;
        }

        var lingua = await CodiceLinguaAsync(utenza, annullamento);
        _email.Accoda(
            utenza.IdTenant!.Value,
            new MessaggioEmail(
                utenza.Email,
                utenza.NomeVisualizzato,
                _localizzatore.TraduciIn(lingua, "La tua password è stata cambiata"),
                _localizzatore.TraduciIn(lingua, "La password del tuo account è stata appena cambiata. Se non sei stato tu, contatta subito l'amministratore del sito.")),
            chiaveIdempotenza: $"recupero-notifica:{richiesta.Id}",
            riservato: false);

        try
        {
            await _audit.RegistraAsync(
                AzioniAudit.RecuperoPasswordCompletato,
                $"Utenza:{utenza.Id}",
                RegistroAudit.EsitoRiuscito,
                utenza.IdTenant,
                utenza.Id,
                dettagli: null,
                annullamento);
        }
        catch (DbUpdateException)
        {
            // Lo stesso link usato due volte in parallelo (conflitto di concorrenza sull'account o chiave di idempotenza
            // della notifica già presente): il secondo non cambia nulla, tutto il salvataggio viene annullato.
            return EsitoReimpostazione.LinkNonValido;
        }

        return EsitoReimpostazione.Riuscita;
    }

    private async Task<(RichiestaRecuperoPassword? Richiesta, Utenza? Utenza)> TrovaPerTokenAsync(string? token, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var idTenant = _tenant.IdTenant;
        var hash = Sha256(token is { Length: > 0 and <= 100 } ? token : "-");

        var richiesta = await _contesto.RichiesteRecuperoPassword.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.HashToken == hash && r.IdTenant == idTenant, annullamento);
        if (richiesta is null
            || richiesta.Stato != StatoRecuperoPassword.LinkInviato
            || richiesta.ScadenzaTokenIlUtc is not { } scadenza
            || scadenza <= adesso
            || richiesta.IdUtenza is not { } idUtenza)
        {
            return (null, null);
        }

        var utenza = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(
            u => u.Id == idUtenza && u.IdTenant == idTenant && u.Stato == StatoUtenza.Attiva && u.Ruolo != RuoloUtenza.PlatformAdmin,
            annullamento);
        return utenza is null ? (null, null) : (richiesta, utenza);
    }

    private async Task<string> CodiceLinguaAsync(Utenza utenza, CancellationToken annullamento)
    {
        if (utenza.IdLingua is not { } idLingua)
        {
            return Lingua.CodiceItaliano;
        }

        return await _contesto.Lingue.AsNoTracking().Where(l => l.Id == idLingua && l.Attiva).Select(l => l.Codice).SingleOrDefaultAsync(annullamento)
            ?? Lingua.CodiceItaliano;
    }

    private static byte[] Sha256(string testo) => SHA256.HashData(Encoding.UTF8.GetBytes(testo));

    private static byte[] HashCodice(byte[] sale, string codice) => SHA256.HashData([.. sale, .. Encoding.UTF8.GetBytes(codice)]);

    private static string Base64Url(byte[] dati) => Convert.ToBase64String(dati).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recupero password: impossibile accodare l'email ({TipoEccezione})")]
    private static partial void RegistraErroreAccodamento(ILogger logger, string tipoEccezione);
}

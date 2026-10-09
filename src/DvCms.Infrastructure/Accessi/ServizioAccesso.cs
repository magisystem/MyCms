using DvCms.Application.Accessi;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Localizzazione;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Accessi;

/// <summary>
/// Accesso degli account (ADR-0016). L'account si cerca solo nel tenant dell'host più i <c>PlatformAdmin</c> globali:
/// è uno dei casi d'uso privilegiati che leggono <c>Utenze</c> senza il filtro globale di tenant, con questo vincolo
/// esplicito nella query. Ogni fallimento è indistinguibile dall'esterno e costa lo stesso tempo di una verifica reale.
/// </summary>
public sealed partial class ServizioAccesso : IServizioAccesso
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IHashPassword _hash;
    private readonly HashFinto _hashFinto;
    private readonly OpzioniSicurezza _opzioni;
    private readonly TimeProvider _orologio;
    private readonly IServizioAudit _audit;
    private readonly ILogger<ServizioAccesso> _logger;

    public ServizioAccesso(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IHashPassword hash,
        HashFinto hashFinto,
        OpzioniSicurezza opzioni,
        TimeProvider orologio,
        IServizioAudit audit,
        ILogger<ServizioAccesso> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _hash = hash;
        _hashFinto = hashFinto;
        _opzioni = opzioni;
        _orologio = orologio;
        _audit = audit;
        _logger = logger;
    }

    public async Task<RisultatoAccesso> AccediAsync(string? email, string? password, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;

        if (!_tenant.Risolto
            || !NormalizzatoreEmail.TentaNormalizza(email, out var normalizzata)
            || string.IsNullOrEmpty(password)
            || password.Length > PoliticaPassword.LunghezzaMassima)
        {
            ConsumaTempoDiVerifica();
            return RisultatoAccesso.Fallito;
        }

        var idTenant = _tenant.IdTenant;
        var candidati = await _contesto.Utenze.IgnoreQueryFilters()
            .Where(utenza => utenza.EmailNormalizzata == normalizzata
                && (utenza.IdTenant == idTenant || utenza.Ruolo == RuoloUtenza.PlatformAdmin))
            .ToListAsync(annullamento);

        if (candidati.Count != 1)
        {
            if (candidati.Count > 1)
            {
                RegistraAmbiguita(_logger, idTenant);
            }

            ConsumaTempoDiVerifica();
            return RisultatoAccesso.Fallito;
        }

        var utenza = candidati[0];
        if (utenza.Stato != StatoUtenza.Attiva || (utenza.BloccatoFinoAlUtc is { } finoA && finoA > adesso))
        {
            ConsumaTempoDiVerifica();
            return RisultatoAccesso.Fallito;
        }

        if (!_hash.Verifica(password, utenza.HashPassword))
        {
            await RegistraTentativoFallitoAsync(utenza, adesso, annullamento);
            return RisultatoAccesso.Fallito;
        }

        utenza.ContatoreAccessiFalliti = 0;
        utenza.BloccatoFinoAlUtc = null;
        utenza.UltimoAccessoIlUtc = adesso;
        if (_hash.RichiedeAggiornamento(utenza.HashPassword))
        {
            utenza.HashPassword = _hash.Calcola(password);
        }

        utenza.AggiornatoIlUtc = adesso;
        await SalvaSenzaBloccareAsync(annullamento);

        if (utenza.Ruolo == RuoloUtenza.PlatformAdmin)
        {
            // L'ingresso del PlatformAdmin in un tenant è un'operazione privilegiata: sempre nel registro di audit.
            await _audit.RegistraAsync(
                AzioniAudit.AccessoPlatformAdmin,
                $"Tenant:{idTenant}",
                RegistroAudit.EsitoRiuscito,
                idTenant,
                utenza.Id,
                dettagli: null,
                annullamento);
        }

        return RisultatoAccesso.Riuscito(await CreaIdentitaAsync(utenza, annullamento));
    }

    public async Task<IdentitaUtenza?> ConvalidaSessioneAsync(Guid idUtenza, Guid timbroSicurezza, CancellationToken annullamento)
    {
        if (!_tenant.Risolto)
        {
            return null;
        }

        var utenza = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(candidata => candidata.Id == idUtenza, annullamento);

        if (utenza is null
            || utenza.Stato != StatoUtenza.Attiva
            || utenza.TimbroSicurezza != timbroSicurezza
            || (utenza.Ruolo != RuoloUtenza.PlatformAdmin && utenza.IdTenant != _tenant.IdTenant))
        {
            return null;
        }

        return await CreaIdentitaAsync(utenza, annullamento);
    }

    public async Task<EsitoCambioPassword> CambiaPasswordAsync(
        Guid idUtenza,
        string? passwordAttuale,
        string? nuovaPassword,
        CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;

        var utenza = await _contesto.Utenze.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidata => candidata.Id == idUtenza, annullamento);
        if (utenza is null
            || utenza.Stato != StatoUtenza.Attiva
            || !_tenant.Risolto
            || (utenza.Ruolo != RuoloUtenza.PlatformAdmin && utenza.IdTenant != _tenant.IdTenant)
            || (utenza.BloccatoFinoAlUtc is { } finoA && finoA > adesso))
        {
            return EsitoCambioPassword.NonConsentito;
        }

        if (string.IsNullOrEmpty(passwordAttuale)
            || passwordAttuale.Length > PoliticaPassword.LunghezzaMassima
            || !_hash.Verifica(passwordAttuale, utenza.HashPassword))
        {
            // Anche il cambio password conta come tentativo: una sessione rubata non può indovinare la password attuale.
            await RegistraTentativoFallitoAsync(utenza, adesso, annullamento);
            return EsitoCambioPassword.PasswordAttualeErrata;
        }

        if (PoliticaPassword.Valida(nuovaPassword) is not null
            || string.Equals(nuovaPassword, passwordAttuale, StringComparison.Ordinal))
        {
            return EsitoCambioPassword.PasswordNonValida;
        }

        utenza.HashPassword = _hash.Calcola(nuovaPassword!);
        utenza.PasswordAggiornataIlUtc = adesso;
        utenza.DeveCambiarePassword = false;
        utenza.TimbroSicurezza = Guid.NewGuid();
        utenza.ContatoreAccessiFalliti = 0;
        utenza.BloccatoFinoAlUtc = null;
        utenza.AggiornatoIlUtc = adesso;
        await _contesto.SaveChangesAsync(annullamento);

        await _audit.RegistraAsync(
            AzioniAudit.CambioPassword,
            $"Utenza:{utenza.Id}",
            RegistroAudit.EsitoRiuscito,
            utenza.IdTenant ?? _tenant.IdTenantOpzionale,
            utenza.Id,
            dettagli: null,
            annullamento);

        return EsitoCambioPassword.Riuscito;
    }

    private async Task RegistraTentativoFallitoAsync(Utenza utenza, DateTime adesso, CancellationToken annullamento)
    {
        utenza.ContatoreAccessiFalliti++;
        if (utenza.ContatoreAccessiFalliti >= _opzioni.TentativiMassimi)
        {
            utenza.BloccatoFinoAlUtc = adesso + _opzioni.DurataBlocco;
            utenza.ContatoreAccessiFalliti = 0;
            RegistraBlocco(_logger, utenza.Id);
        }

        utenza.AggiornatoIlUtc = adesso;
        await SalvaSenzaBloccareAsync(annullamento);
    }

    private async Task SalvaSenzaBloccareAsync(CancellationToken annullamento)
    {
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Tentativi simultanei sullo stesso account: l'aggiornamento dei contatori non è essenziale, si ignora.
            RegistraConflitto(_logger);
        }
    }

    private async Task<IdentitaUtenza> CreaIdentitaAsync(Utenza utenza, CancellationToken annullamento)
    {
        var codiceLingua = utenza.IdLingua is { } idLingua
            ? await _contesto.Lingue.AsNoTracking()
                .Where(lingua => lingua.Id == idLingua && lingua.Attiva)
                .Select(lingua => lingua.Codice)
                .SingleOrDefaultAsync(annullamento)
            : null;

        return new IdentitaUtenza(
            utenza.Id,
            utenza.Ruolo,
            utenza.IdTenant,
            utenza.Email,
            utenza.NomeVisualizzato,
            utenza.TimbroSicurezza,
            utenza.DeveCambiarePassword,
            codiceLingua ?? Lingua.CodiceItaliano);
    }

    private void ConsumaTempoDiVerifica() => _hash.Verifica("password-inesistente", _hashFinto.Valore);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account bloccato per 5 minuti dopo troppi tentativi falliti: {IdUtenza}")]
    private static partial void RegistraBlocco(ILogger logger, Guid idUtenza);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Più account corrispondono all'email nel tenant {IdTenant}: accesso rifiutato")]
    private static partial void RegistraAmbiguita(ILogger logger, Guid idTenant);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conflitto di concorrenza nell'aggiornamento dei contatori di accesso")]
    private static partial void RegistraConflitto(ILogger logger);
}

/// <summary>Hash di una password inesistente, usato per rendere uguale il tempo dei fallimenti prima della verifica.</summary>
public sealed class HashFinto
{
    private readonly Lazy<string> _valore;

    public HashFinto(IHashPassword hash)
    {
        _valore = new Lazy<string>(() => hash.Calcola(Guid.NewGuid().ToString("N")));
    }

    public string Valore => _valore.Value;
}

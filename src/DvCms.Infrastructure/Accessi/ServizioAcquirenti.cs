using System.Security.Cryptography;
using System.Text;
using DvCms.Application.Accessi;
using DvCms.Application.Email;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Extender;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Localizzazione;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Accessi;

/// <summary>
/// Registrazione degli acquirenti (vedi <see cref="IServizioAcquirenti"/>). Come per gli inviti, legge <c>Utenze</c> senza il filtro
/// globale ma sempre con il tenant dell'host nella query. Nessuna risposta distingue un indirizzo nuovo da uno già registrato.
/// </summary>
public sealed class ServizioAcquirenti : IServizioAcquirenti
{
    private const int LunghezzaMassimaNome = 100;

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IHashPassword _hash;
    private readonly IAccodatoreEmail _email;
    private readonly ILocalizzatore _localizzatore;
    private readonly IServizioAudit _audit;
    private readonly OpzioniAcquirenti _opzioni;
    private readonly TimeProvider _orologio;

    public ServizioAcquirenti(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IHashPassword hash,
        IAccodatoreEmail email,
        ILocalizzatore localizzatore,
        IServizioAudit audit,
        OpzioniAcquirenti opzioni,
        TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _hash = hash;
        _email = email;
        _localizzatore = localizzatore;
        _audit = audit;
        _opzioni = opzioni;
        _orologio = orologio;
    }

    public Task<bool> RegistrazioneAbilitataAsync(CancellationToken annullamento)
    {
        if (!_tenant.Risolto)
        {
            return Task.FromResult(false);
        }

        var idSito = _tenant.IdSito;
        return _contesto.Siti.AsNoTracking().AnyAsync(sito => sito.Id == idSito && sito.CommercioAttivo && sito.RegistrazioneAcquirentiAbilitata, annullamento);
    }

    public async Task<bool> RegistrazioneDisponibileAsync(CancellationToken annullamento)
    {
        if (!await RegistrazioneAbilitataAsync(annullamento))
        {
            return false;
        }

        var idTenant = _tenant.IdTenant;
        return await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AnyAsync(impostazioni => impostazioni.IdTenant == idTenant, annullamento);
    }

    public async Task<RisultatoRegistrazione> RegistraAsync(DatiRegistrazioneAcquirente dati, DatiLinkInvito link, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);
        ArgumentNullException.ThrowIfNull(link);

        if (!await RegistrazioneDisponibileAsync(annullamento) || !(link.Schema is "http" or "https" && link.Porta is null or (> 0 and <= 65535)))
        {
            return new RisultatoRegistrazione(EsitoRegistrazione.NonDisponibile);
        }

        var errori = new List<string>();
        if (!NormalizzatoreEmail.TentaNormalizza(dati.Email, out var normalizzata))
        {
            errori.Add("Email");
        }

        var nome = dati.NomeVisualizzato?.Trim() ?? string.Empty;
        if (nome.Length == 0 || nome.Length > LunghezzaMassimaNome || nome.Any(char.IsControl))
        {
            errori.Add("Nome");
        }

        if (errori.Count > 0)
        {
            return new RisultatoRegistrazione(EsitoRegistrazione.DatiNonValidi, errori);
        }

        var idTenant = _tenant.IdTenant;
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var esistente = await _contesto.Utenze.IgnoreQueryFilters()
            .SingleOrDefaultAsync(utenza => utenza.IdTenant == idTenant && utenza.EmailNormalizzata == normalizzata, annullamento);

        Utenza acquirente;
        if (esistente is null)
        {
            // Password sconosciuta a tutti: la sceglie chi conferma l'email dal link. Fino ad allora l'account non può accedere.
            var inutilizzabile = _hash.Calcola(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            acquirente = Utenza.Crea(RuoloUtenza.Utente, idTenant, dati.Email!, nome, inutilizzabile, deveCambiarePassword: true, _orologio);
            acquirente.Stato = StatoUtenza.InAttesaDiConferma;
            _contesto.Utenze.Add(acquirente);
        }
        else if (esistente.Ruolo == RuoloUtenza.Utente && esistente.Stato == StatoUtenza.InAttesaDiConferma)
        {
            // Già in attesa: si rinvia il link, con un limite orario per non usare il sito per riempire di email una casella.
            var unOraFa = adesso - TimeSpan.FromHours(1);
            var recenti = await _contesto.ConfermeEmailUtenza.IgnoreQueryFilters()
                .CountAsync(c => c.IdUtenza == esistente.Id && c.CreatoIlUtc > unOraFa, annullamento);
            if (recenti >= _opzioni.RichiesteOrarieMassimeConferma)
            {
                return new RisultatoRegistrazione(EsitoRegistrazione.Accettata);
            }

            acquirente = esistente;
        }
        else
        {
            // Indirizzo già registrato (o account di altro tipo): nessuna email, nessuna differenza visibile.
            return new RisultatoRegistrazione(EsitoRegistrazione.Accettata);
        }

        try
        {
            await PreparaConfermaAsync(acquirente, link, adesso, annullamento);
        }
        catch (Exception eccezione) when (eccezione is InvalidOperationException or CryptographicException)
        {
            // Chiave dei segreti mancante o non valida: l'email riservata non si può accodare, quindi la registrazione non è disponibile.
            _contesto.ChangeTracker.Clear();
            return new RisultatoRegistrazione(EsitoRegistrazione.NonDisponibile);
        }

        await _contesto.SaveChangesAsync(annullamento);
        // Nessun attore: chi si registra non è ancora autenticato, e un account mai confermato deve poter essere tolto dalla manutenzione.
        await _audit.RegistraAsync(AzioniAudit.RegistrazioneAcquirente, $"Utenza:{acquirente.Id}", RegistroAudit.EsitoRiuscito, idTenant, null, dettagli: null, annullamento);
        return new RisultatoRegistrazione(EsitoRegistrazione.Accettata);
    }

    public async Task<bool> LinkValidoAsync(string? token, CancellationToken annullamento)
    {
        var (conferma, utenza) = await TrovaAsync(token, annullamento);
        return conferma is not null && utenza is not null;
    }

    public async Task<EsitoReimpostazione> ConfermaAsync(string? token, string? password, CancellationToken annullamento)
    {
        var (conferma, utenza) = await TrovaAsync(token, annullamento);
        if (conferma is null || utenza is null)
        {
            return EsitoReimpostazione.LinkNonValido;
        }

        if (PoliticaPassword.Valida(password) is not null)
        {
            // Il link resta valido: si può riprovare con una password accettabile.
            return EsitoReimpostazione.PasswordNonValida;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        utenza.HashPassword = _hash.Calcola(password!);
        utenza.PasswordAggiornataIlUtc = adesso;
        utenza.DeveCambiarePassword = false;
        utenza.Stato = StatoUtenza.Attiva;
        utenza.TimbroSicurezza = Guid.NewGuid();
        utenza.ContatoreAccessiFalliti = 0;
        utenza.BloccatoFinoAlUtc = null;
        utenza.AggiornatoIlUtc = adesso;

        conferma.Stato = StatoConfermaEmail.Confermata;
        conferma.ConfermataIlUtc = adesso;
        var altre = await _contesto.ConfermeEmailUtenza.IgnoreQueryFilters()
            .Where(c => c.IdUtenza == utenza.Id && c.Id != conferma.Id && c.Stato == StatoConfermaEmail.Inviata)
            .ToListAsync(annullamento);
        foreach (var altra in altre)
        {
            altra.Stato = StatoConfermaEmail.Annullata;
        }

        try
        {
            await _audit.RegistraAsync(AzioniAudit.ConfermaAcquirente, $"Utenza:{utenza.Id}", RegistroAudit.EsitoRiuscito, utenza.IdTenant, utenza.Id, dettagli: null, annullamento);
        }
        catch (DbUpdateException)
        {
            // Lo stesso link usato due volte in parallelo (conflitto di concorrenza sull'account): il secondo non cambia nulla.
            return EsitoReimpostazione.LinkNonValido;
        }

        return EsitoReimpostazione.Riuscita;
    }

    /// <summary>Annulla le conferme aperte, ne crea una nuova e accoda l'email riservata. Non salva: lo fa chi chiama.</summary>
    private async Task PreparaConfermaAsync(Utenza acquirente, DatiLinkInvito link, DateTime adesso, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var conferma = new ConfermaEmailUtenza
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = idTenant,
            IdUtenza = acquirente.Id,
            HashToken = SHA256.HashData(Encoding.UTF8.GetBytes(token)),
            ScadeIlUtc = adesso + _opzioni.ValiditaConferma,
            CreatoIlUtc = adesso,
        };

        // Il link usa il dominio registrato e verificato dell'host (mai l'intestazione Host della richiesta così com'è).
        var url = $"{link.Schema}://{_tenant.Host}{(link.Porta is { } porta ? $":{porta}" : string.Empty)}/account/conferma?token={token}";
        var nomeSito = await _contesto.Siti.AsNoTracking().Where(s => s.Id == _tenant.IdSito).Select(s => s.Nome).SingleAsync(annullamento);
        var lingue = await LingueDelSito.ElencaAsync(_contesto, _tenant.IdSito, annullamento);
        var lingua = lingue.FirstOrDefault(l => l.Predefinita)?.Codice ?? Lingua.CodiceItaliano;

        _email.Accoda(
            idTenant,
            new MessaggioEmail(
                acquirente.Email,
                acquirente.NomeVisualizzato,
                _localizzatore.TraduciIn(lingua, "Conferma la tua registrazione a {0}", nomeSito),
                _localizzatore.TraduciIn(
                    lingua,
                    "Per completare la registrazione a {0} apri questo link entro {2} ore e scegli la tua password: {1}. Il link si può usare una sola volta. Se non sei stato tu, ignora questo messaggio.",
                    nomeSito,
                    url,
                    (int)_opzioni.ValiditaConferma.TotalHours)),
            chiaveIdempotenza: $"conferma:{conferma.Id}",
            riservato: true);

        var aperte = await _contesto.ConfermeEmailUtenza.IgnoreQueryFilters()
            .Where(c => c.IdUtenza == acquirente.Id && c.Stato == StatoConfermaEmail.Inviata)
            .ToListAsync(annullamento);
        foreach (var vecchia in aperte)
        {
            vecchia.Stato = StatoConfermaEmail.Annullata;
        }

        _contesto.ConfermeEmailUtenza.Add(conferma);
    }

    private async Task<(ConfermaEmailUtenza? Conferma, Utenza? Utenza)> TrovaAsync(string? token, CancellationToken annullamento)
    {
        if (!_tenant.Risolto)
        {
            return (null, null);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var idTenant = _tenant.IdTenant;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token is { Length: > 0 and <= 100 } ? token : "-"));

        var conferma = await _contesto.ConfermeEmailUtenza.IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.HashToken == hash && c.IdTenant == idTenant, annullamento);
        if (conferma is null || conferma.Stato != StatoConfermaEmail.Inviata || conferma.ScadeIlUtc <= adesso)
        {
            return (null, null);
        }

        var utenza = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidata => candidata.Id == conferma.IdUtenza
                && candidata.IdTenant == idTenant
                && candidata.Ruolo == RuoloUtenza.Utente
                && candidata.Stato == StatoUtenza.InAttesaDiConferma,
            annullamento);
        return utenza is null ? (null, null) : (conferma, utenza);
    }
}

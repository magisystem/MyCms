using System.Security.Cryptography;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Cms;
using DvCms.Contracts.Email;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Piattaforma;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Accessi;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Cms;
using DvCms.Models.Localizzazione;
using DvCms.Models.Tenancy;
using DvCms.Services.Accessi;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Piattaforma;

/// <summary>
/// Operazioni del menu "Platform" (ADR-0014). È un caso d'uso privilegiato: lavora su più tenant, quindi usa
/// <c>IgnoreQueryFilters()</c>, e per questo verifica sempre sul database che l'attore sia un <c>PlatformAdmin</c> attivo.
/// </summary>
public sealed class ServizioPiattaforma : IServizioPiattaforma
{
    private const int LunghezzaMassimaNome = 200;
    private const int LunghezzaMassimaFusoOrario = 64;

    private readonly ContestoDatabase _contesto;
    private readonly IServizioAudit _audit;
    private readonly IHashPassword _hash;
    private readonly IAccodatoreEmail _email;
    private readonly IComponitoreEmail _modelli;
    private readonly OpzioniInviti _opzioniInviti;
    private readonly IInvalidazioneCache _invalidazione;
    private readonly TimeProvider _orologio;

    public ServizioPiattaforma(
        ContestoDatabase contesto,
        IServizioAudit audit,
        IHashPassword hash,
        IAccodatoreEmail email,
        IComponitoreEmail modelli,
        OpzioniInviti opzioniInviti,
        IInvalidazioneCache invalidazione,
        TimeProvider orologio)
    {
        _invalidazione = invalidazione;
        _contesto = contesto;
        _audit = audit;
        _hash = hash;
        _email = email;
        _modelli = modelli;
        _opzioniInviti = opzioniInviti;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<RiepilogoTenant>> ElencaTenantAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);

        return await _contesto.Tenant.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(tenant => tenant.Primario).ThenBy(tenant => tenant.Nome)
            .Select(tenant => new RiepilogoTenant(
                tenant.Id,
                tenant.Nome,
                tenant.Stato,
                tenant.Primario,
                _contesto.Siti.IgnoreQueryFilters().Count(sito => sito.IdTenant == tenant.Id),
                _contesto.Utenze.IgnoreQueryFilters().Count(utenza => utenza.IdTenant == tenant.Id && utenza.Ruolo == RuoloUtenza.TenantAdmin),
                tenant.CreatoIlUtc))
            .ToListAsync(annullamento);
    }

    public async Task<DettaglioTenant?> LeggiTenantAsync(Guid idAttore, Guid idTenant, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);

        var tenant = await _contesto.Tenant.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(candidato => candidato.Id == idTenant, annullamento);
        if (tenant is null)
        {
            return null;
        }

        var siti = await _contesto.Siti.IgnoreQueryFilters().AsNoTracking()
            .Where(sito => sito.IdTenant == idTenant).OrderBy(sito => sito.CreatoIlUtc).ToListAsync(annullamento);
        var domini = await _contesto.DominiSito.IgnoreQueryFilters().AsNoTracking()
            .Where(dominio => dominio.IdTenant == idTenant).OrderBy(dominio => dominio.Host).ToListAsync(annullamento);
        var account = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking()
            .Where(utenza => utenza.IdTenant == idTenant).OrderBy(utenza => utenza.Email).ToListAsync(annullamento);
        var lingueSito = await (
            from ls in _contesto.LingueSito.IgnoreQueryFilters().AsNoTracking()
            join lingua in _contesto.Lingue.AsNoTracking() on ls.IdLingua equals lingua.Id
            where ls.IdTenant == idTenant
            orderby ls.Predefinita descending, lingua.Codice
            select new { ls.IdSito, Lingua = new LinguaDelSito(lingua.Codice, lingua.Nome, ls.Predefinita) })
            .ToListAsync(annullamento);
        var lingueDisponibili = await _contesto.Lingue.AsNoTracking().Where(l => l.Attiva).OrderBy(l => l.Codice)
            .Select(l => new LinguaDelSito(l.Codice, l.Nome, l.Predefinita)).ToListAsync(annullamento);
        var idVerificatori = domini.Where(dominio => dominio.VerificatoDaIdUtenza is not null).Select(dominio => dominio.VerificatoDaIdUtenza!.Value).Distinct().ToList();
        var verificatori = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking()
            .Where(utenza => idVerificatori.Contains(utenza.Id))
            .ToDictionaryAsync(utenza => utenza.Id, utenza => utenza.Email, annullamento);

        return new DettaglioTenant(
            tenant.Id,
            tenant.Nome,
            tenant.Stato,
            tenant.Primario,
            tenant.CreatoIlUtc,
            [.. siti.Select(sito => new DettaglioSito(
                sito.Id,
                sito.Nome,
                sito.FusoOrario,
                sito.Stato,
                [.. domini.Where(dominio => dominio.IdSito == sito.Id).Select(dominio => new DettaglioDominio(
                    dominio.Id,
                    dominio.Host,
                    dominio.Attivo,
                    dominio.SoloSviluppo,
                    dominio.VerificatoIlUtc,
                    dominio.VerificatoDaIdUtenza is { } id && verificatori.TryGetValue(id, out var email) ? email : null))],
                [.. lingueSito.Where(l => l.IdSito == sito.Id).Select(l => l.Lingua)],
                sito.CommercioAttivo,
                sito.RegistrazioneAcquirentiAbilitata))],
            [.. account.Select(utenza => new DettaglioAccount(
                utenza.Id,
                utenza.Email,
                utenza.NomeVisualizzato,
                utenza.Ruolo,
                utenza.Stato,
                utenza.DeveCambiarePassword,
                utenza.UltimoAccessoIlUtc,
                utenza.DueFattoriAttivi))],
            lingueDisponibili,
            await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AnyAsync(impostazioni => impostazioni.IdTenant == idTenant, annullamento));
    }

    public Task<EsitoOperazione> CreaTenantAsync(Guid idAttore, DatiNuovoTenant dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        return EseguiAsync(idAttore, AzioniAudit.CreazioneTenant, "Tenant", null, async attore =>
        {
            if (!TentaNome(dati.Nome, out var nome)
                || !TentaNome(dati.NomeSito, out var nomeSito)
                || !TentaFusoOrario(dati.FusoOrario, out var fuso)
                || !TentaHostUtente(dati.Host, out var host))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            if (await HostEsistenteAsync(host, annullamento))
            {
                return Esito.Non(CodiceEsito.GiaPresente);
            }

            var tenant = Tenant.Crea(nome, primario: false, _orologio);
            var sito = Sito.Crea(tenant.Id, nomeSito, fuso, _orologio);
            _contesto.Tenant.Add(tenant);
            _contesto.Siti.Add(sito);
            _contesto.LingueSito.Add(LinguaSito.Crea(tenant.Id, sito.Id, Lingua.IdItaliano, predefinita: true, _orologio));
            _contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, _orologio));
            _contesto.DominiSito.Add(NuovoDominio(tenant.Id, sito.Id, host, dati.DominioVerificato, attore));
            return Esito.Ok(tenant.Id, tenant.Id, $"tenant '{tenant.Nome}', dominio '{host}'{(dati.DominioVerificato ? " verificato" : " da verificare")}");
        }, annullamento);
    }

    public Task<EsitoOperazione> RinominaTenantAsync(Guid idAttore, Guid idTenant, string? nome, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.ModificaTenant, "Tenant", idTenant, async _ =>
        {
            if (!TentaNome(nome, out var nomeValido))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            var tenant = await _contesto.Tenant.IgnoreQueryFilters().SingleOrDefaultAsync(candidato => candidato.Id == idTenant, annullamento);
            if (tenant is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            tenant.Nome = nomeValido;
            tenant.AggiornatoIlUtc = Adesso();
            return Esito.Ok(tenant.Id, tenant.Id, $"nuovo nome '{nomeValido}'");
        }, annullamento);

    public Task<EsitoOperazione> ImpostaTenantSospesoAsync(Guid idAttore, Guid idTenant, bool sospeso, CancellationToken annullamento) =>
        EseguiAsync(idAttore, sospeso ? AzioniAudit.SospensioneTenant : AzioniAudit.RiattivazioneTenant, "Tenant", idTenant, async _ =>
        {
            var tenant = await _contesto.Tenant.IgnoreQueryFilters().SingleOrDefaultAsync(candidato => candidato.Id == idTenant, annullamento);
            if (tenant is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            if (sospeso && tenant.Primario)
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            tenant.Stato = sospeso ? StatoTenant.Sospeso : StatoTenant.Attivo;
            tenant.AggiornatoIlUtc = Adesso();
            return Esito.Ok(tenant.Id, tenant.Id, null);
        }, annullamento);

    public Task<EsitoOperazione> CreaSitoAsync(Guid idAttore, Guid idTenant, string? nome, string? fusoOrario, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.CreazioneSito, "Sito", idTenant, async _ =>
        {
            if (!TentaNome(nome, out var nomeValido) || !TentaFusoOrario(fusoOrario, out var fuso))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            if (!await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(candidato => candidato.Id == idTenant, annullamento))
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            var sito = Sito.Crea(idTenant, nomeValido, fuso, _orologio);
            _contesto.Siti.Add(sito);
            _contesto.LingueSito.Add(LinguaSito.Crea(idTenant, sito.Id, Lingua.IdItaliano, predefinita: true, _orologio));
            return Esito.Ok(sito.Id, idTenant, $"sito '{sito.Nome}'");
        }, annullamento);

    public Task<EsitoOperazione> ModificaSitoAsync(
        Guid idAttore,
        Guid idTenant,
        Guid idSito,
        string? nome,
        string? fusoOrario,
        bool attivo,
        CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.ModificaSito, "Sito", idTenant, async _ =>
        {
            if (!TentaNome(nome, out var nomeValido) || !TentaFusoOrario(fusoOrario, out var fuso))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            var sito = await _contesto.Siti.IgnoreQueryFilters().SingleOrDefaultAsync(candidato => candidato.Id == idSito && candidato.IdTenant == idTenant, annullamento);
            if (sito is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            sito.Nome = nomeValido;
            sito.FusoOrario = fuso;
            sito.Stato = attivo ? StatoSito.Attivo : StatoSito.Disattivato;
            sito.AggiornatoIlUtc = Adesso();
            _invalidazione.Registra(idTenant, sito.Id);
            return Esito.Ok(sito.Id, idTenant, $"sito '{sito.Nome}', stato {sito.Stato}");
        }, annullamento);

    public Task<EsitoOperazione> ImpostaCommercioAsync(Guid idAttore, Guid idTenant, Guid idSito, bool attivo, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.ImpostazioneCommercio, "Sito", idTenant, async _ =>
        {
            var sito = await _contesto.Siti.IgnoreQueryFilters().SingleOrDefaultAsync(candidato => candidato.Id == idSito && candidato.IdTenant == idTenant, annullamento);
            if (sito is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            sito.CommercioAttivo = attivo;
            if (!attivo)
            {
                sito.RegistrazioneAcquirentiAbilitata = false;
            }

            sito.AggiornatoIlUtc = Adesso();
            _invalidazione.Registra(idTenant, sito.Id);
            return Esito.Ok(sito.Id, idTenant, $"negozio {(attivo ? "attivo" : "non attivo")}");
        }, annullamento);

    public Task<EsitoOperazione> ImpostaRegistrazioneAcquirentiAsync(Guid idAttore, Guid idTenant, Guid idSito, bool abilitata, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.ImpostazioneRegistrazioneAcquirenti, "Sito", idTenant, async _ =>
        {
            var sito = await _contesto.Siti.IgnoreQueryFilters().SingleOrDefaultAsync(candidato => candidato.Id == idSito && candidato.IdTenant == idTenant, annullamento);
            if (sito is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            if (abilitata && !sito.CommercioAttivo)
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            sito.RegistrazioneAcquirentiAbilitata = abilitata;
            sito.AggiornatoIlUtc = Adesso();
            _invalidazione.Registra(idTenant, sito.Id);
            return Esito.Ok(sito.Id, idTenant, $"registrazione acquirenti {(abilitata ? "abilitata" : "disabilitata")}");
        }, annullamento);

    public Task<EsitoOperazione> AbilitaLinguaAsync(Guid idAttore, Guid idTenant, Guid idSito, string? codiceLingua, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.AbilitazioneLingua, "Sito", idTenant, async _ =>
        {
            if (!await _contesto.Siti.IgnoreQueryFilters().AnyAsync(s => s.Id == idSito && s.IdTenant == idTenant, annullamento))
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            var lingua = await TrovaLinguaAttivaAsync(codiceLingua, annullamento);
            if (lingua is null)
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            if (await _contesto.LingueSito.IgnoreQueryFilters().AnyAsync(ls => ls.IdSito == idSito && ls.IdLingua == lingua.Id, annullamento))
            {
                return Esito.Non(CodiceEsito.GiaPresente);
            }

            // La prima lingua di un sito (non dovrebbe capitare) diventa la predefinita.
            var haPredefinita = await _contesto.LingueSito.IgnoreQueryFilters().AnyAsync(ls => ls.IdSito == idSito && ls.Predefinita, annullamento);
            var riga = LinguaSito.Crea(idTenant, idSito, lingua.Id, predefinita: !haPredefinita, _orologio);
            _contesto.LingueSito.Add(riga);
            _invalidazione.Registra(idTenant, idSito);
            return Esito.Ok(riga.Id, idTenant, $"lingua {lingua.Codice}");
        }, annullamento);

    public Task<EsitoOperazione> DisabilitaLinguaAsync(Guid idAttore, Guid idTenant, Guid idSito, string? codiceLingua, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.DisabilitazioneLingua, "Sito", idTenant, async _ =>
        {
            var riga = await TrovaLinguaSitoAsync(idTenant, idSito, codiceLingua, annullamento);
            if (riga is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            if (riga.Predefinita
                || await _contesto.Pagine.IgnoreQueryFilters().AnyAsync(p => p.IdSito == idSito && p.IdLingua == riga.IdLingua, annullamento))
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            _contesto.LingueSito.Remove(riga);
            _invalidazione.Registra(idTenant, idSito);
            return Esito.Ok(riga.Id, idTenant, $"lingua {codiceLingua}");
        }, annullamento);

    public async Task<EsitoOperazione> ImpostaLinguaPredefinitaAsync(Guid idAttore, Guid idTenant, Guid idSito, string? codiceLingua, CancellationToken annullamento)
    {
        // L'indice univoco "una sola predefinita" impedisce di cambiare le due righe in un solo salvataggio:
        // la modifica avviene in due passi dentro una transazione.
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        var esito = await EseguiAsync(idAttore, AzioniAudit.ImpostazioneLinguaPredefinita, "Sito", idTenant, async _ =>
        {
            var nuova = await TrovaLinguaSitoAsync(idTenant, idSito, codiceLingua, annullamento);
            if (nuova is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            if (nuova.Predefinita)
            {
                return Esito.Ok(nuova.Id, idTenant, "invariata");
            }

            if (await _contesto.Pagine.IgnoreQueryFilters().AnyAsync(p => p.IdSito == idSito && p.Stato == StatoPagina.Pubblicata, annullamento))
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            var vecchia = await _contesto.LingueSito.IgnoreQueryFilters().SingleOrDefaultAsync(ls => ls.IdSito == idSito && ls.Predefinita, annullamento);
            if (vecchia is not null)
            {
                vecchia.Predefinita = false;
                await _contesto.SaveChangesAsync(annullamento);
            }

            nuova.Predefinita = true;
            _invalidazione.Registra(idTenant, idSito);
            return Esito.Ok(nuova.Id, idTenant, $"lingua predefinita {codiceLingua}");
        }, annullamento);
        // Si conferma se è riuscita o se è stata negata (il tentativo negato va nell'audit); ogni altro esito annulla anche il primo passo.
        if (esito.Riuscito || esito.Codice == CodiceEsito.NonAutorizzato)
        {
            await transazione.CommitAsync(annullamento);
        }

        return esito;
    }

    private Task<Lingua?> TrovaLinguaAttivaAsync(string? codice, CancellationToken annullamento)
    {
        if (string.IsNullOrWhiteSpace(codice))
        {
            return Task.FromResult<Lingua?>(null);
        }

        var normalizzato = codice.Trim().ToLowerInvariant();
        return _contesto.Lingue.AsNoTracking().SingleOrDefaultAsync(l => l.Codice == normalizzato && l.Attiva, annullamento);
    }

    private async Task<LinguaSito?> TrovaLinguaSitoAsync(Guid idTenant, Guid idSito, string? codice, CancellationToken annullamento)
    {
        var lingua = await TrovaLinguaAttivaAsync(codice, annullamento);
        return lingua is null
            ? null
            : await _contesto.LingueSito.IgnoreQueryFilters().SingleOrDefaultAsync(
                ls => ls.IdTenant == idTenant && ls.IdSito == idSito && ls.IdLingua == lingua.Id,
                annullamento);
    }

    public Task<EsitoOperazione> AggiungiDominioAsync(
        Guid idAttore,
        Guid idTenant,
        Guid idSito,
        string? host,
        bool verificato,
        CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.CreazioneDominio, "Dominio", idTenant, async attore =>
        {
            if (!TentaHostUtente(host, out var hostValido))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            if (!await _contesto.Siti.IgnoreQueryFilters().AnyAsync(candidato => candidato.Id == idSito && candidato.IdTenant == idTenant, annullamento))
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            if (await HostEsistenteAsync(hostValido, annullamento))
            {
                return Esito.Non(CodiceEsito.GiaPresente);
            }

            var dominio = NuovoDominio(idTenant, idSito, hostValido, verificato, attore);
            _contesto.DominiSito.Add(dominio);
            return Esito.Ok(dominio.Id, idTenant, $"dominio '{hostValido}'{(verificato ? " verificato" : " da verificare")}");
        }, annullamento);

    public Task<EsitoOperazione> VerificaDominioAsync(Guid idAttore, Guid idTenant, Guid idDominio, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.VerificaDominio, "Dominio", idTenant, async attore =>
        {
            var dominio = await TrovaDominioAsync(idTenant, idDominio, annullamento);
            if (dominio is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            if (dominio.VerificatoIlUtc is not null)
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            dominio.VerificatoIlUtc = Adesso();
            dominio.VerificatoDaIdUtenza = attore.Id;
            dominio.AggiornatoIlUtc = dominio.VerificatoIlUtc.Value;
            return Esito.Ok(dominio.Id, idTenant, $"dominio '{dominio.Host}'");
        }, annullamento);

    public Task<EsitoOperazione> ImpostaDominioAttivoAsync(Guid idAttore, Guid idTenant, Guid idDominio, bool attivo, CancellationToken annullamento) =>
        EseguiAsync(idAttore, attivo ? AzioniAudit.RiattivazioneDominio : AzioniAudit.DisattivazioneDominio, "Dominio", idTenant, async _ =>
        {
            var dominio = await TrovaDominioAsync(idTenant, idDominio, annullamento);
            if (dominio is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            dominio.Attivo = attivo;
            dominio.AggiornatoIlUtc = Adesso();
            return Esito.Ok(dominio.Id, idTenant, $"dominio '{dominio.Host}'");
        }, annullamento);

    public async Task<EsitoNuovoAmministratore> CreaTenantAdminAsync(
        Guid idAttore,
        Guid idTenant,
        string? email,
        string? nomeVisualizzato,
        CancellationToken annullamento)
    {
        var password = GeneratorePasswordTemporanea.Genera();
        Guid? idUtenza = null;
        var esito = await EseguiAsync(idAttore, AzioniAudit.CreazioneTenantAdmin, "Utenza", idTenant, async _ =>
        {
            if (!NormalizzatoreEmail.TentaNormalizza(email, out var normalizzata)
                || !TentaNome(nomeVisualizzato, out var nome))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            if (!await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(candidato => candidato.Id == idTenant, annullamento))
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            // Un'email di PlatformAdmin non può essere usata da un account di tenant: renderebbe ambiguo l'accesso (ADR-0011).
            var occupata = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(
                candidata => candidata.EmailNormalizzata == normalizzata
                    && (candidata.IdTenant == idTenant || candidata.Ruolo == RuoloUtenza.PlatformAdmin),
                annullamento);
            if (occupata)
            {
                return Esito.Non(CodiceEsito.GiaPresente);
            }

            var utenza = Utenza.Crea(RuoloUtenza.TenantAdmin, idTenant, email!, nome, _hash.Calcola(password), deveCambiarePassword: true, _orologio);
            _contesto.Utenze.Add(utenza);
            idUtenza = utenza.Id;
            return Esito.Ok(utenza.Id, idTenant, $"TenantAdmin '{utenza.Email}'");
        }, annullamento);

        return esito.Riuscito
            ? new EsitoNuovoAmministratore(CodiceEsito.Riuscito, idUtenza, password)
            : new EsitoNuovoAmministratore(esito.Codice, null, null);
    }

    public Task<EsitoOperazione> CreaTenantAdminConInvitoAsync(
        Guid idAttore,
        Guid idTenant,
        string? email,
        string? nomeVisualizzato,
        DatiLinkInvito link,
        CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(link);

        return EseguiAsync(idAttore, AzioniAudit.CreazioneTenantAdmin, "Utenza", idTenant, async attore =>
        {
            if (!NormalizzatoreEmail.TentaNormalizza(email, out var normalizzata)
                || !TentaNome(nomeVisualizzato, out var nome)
                || !TentaLink(link))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            if (!await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(candidato => candidato.Id == idTenant, annullamento))
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            var occupata = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(
                candidata => candidata.EmailNormalizzata == normalizzata
                    && (candidata.IdTenant == idTenant || candidata.Ruolo == RuoloUtenza.PlatformAdmin),
                annullamento);
            if (occupata)
            {
                return Esito.Non(CodiceEsito.GiaPresente);
            }

            // La password iniziale è casuale, non viene comunicata a nessuno: l'accesso passa dal link dell'invito.
            var utenza = Utenza.Crea(RuoloUtenza.TenantAdmin, idTenant, email!, nome, _hash.Calcola(GeneratorePasswordTemporanea.Genera()), deveCambiarePassword: true, _orologio);
            var codice = await PreparaInvitoAsync(attore, utenza, link, annullamento);
            if (codice != CodiceEsito.Riuscito)
            {
                return Esito.Non(codice);
            }

            _contesto.Utenze.Add(utenza);
            return Esito.Ok(utenza.Id, idTenant, $"TenantAdmin '{utenza.Email}' con invito");
        }, annullamento);
    }

    public Task<EsitoOperazione> InviaInvitoAsync(Guid idAttore, Guid idTenant, Guid idUtenza, DatiLinkInvito link, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(link);

        return EseguiAsync(idAttore, AzioniAudit.InvitoInviato, "Utenza", idTenant, async attore =>
        {
            if (!TentaLink(link))
            {
                return Esito.Non(CodiceEsito.DatiNonValidi);
            }

            var utenza = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(candidata => candidata.Id == idUtenza && candidata.IdTenant == idTenant, annullamento);
            if (utenza is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            // Solo chi non ha ancora scelto una password propria: dopo, la via è il recupero password.
            if (utenza.Ruolo != RuoloUtenza.TenantAdmin || utenza.Stato != StatoUtenza.Attiva || !utenza.DeveCambiarePassword)
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            var codice = await PreparaInvitoAsync(attore, utenza, link, annullamento);
            return codice == CodiceEsito.Riuscito
                ? Esito.Ok(utenza.Id, idTenant, $"invito a '{utenza.Email}'")
                : Esito.Non(codice);
        }, annullamento);
    }

    /// <summary>
    /// Annulla gli inviti aperti dell'account, ne crea uno nuovo e accoda l'email (cifrata nell'outbox) con il link sul dominio
    /// verificato del tenant. Non salva: lo fa chi chiama insieme al resto dell'operazione.
    /// </summary>
    private async Task<CodiceEsito> PreparaInvitoAsync(Utenza attore, Utenza invitato, DatiLinkInvito link, CancellationToken annullamento)
    {
        var idTenant = invitato.IdTenant!.Value;
        if (!await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AnyAsync(impostazioni => impostazioni.IdTenant == idTenant, annullamento))
        {
            return CodiceEsito.EmailNonConfigurata;
        }

        var dominio = await _contesto.DominiSito.IgnoreQueryFilters().AsNoTracking()
            .Where(candidato => candidato.IdTenant == idTenant && candidato.Attivo && candidato.VerificatoIlUtc != null)
            .OrderBy(candidato => candidato.SoloSviluppo).ThenBy(candidato => candidato.CreatoIlUtc)
            .Select(candidato => candidato.Host)
            .FirstOrDefaultAsync(annullamento);
        if (dominio is null)
        {
            return CodiceEsito.NonConsentito;
        }

        var nomeTenant = await _contesto.Tenant.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == idTenant).Select(t => t.Nome).SingleAsync(annullamento);
        var codiceLingua = invitato.IdLingua is { } idLingua
            ? await _contesto.Lingue.AsNoTracking().Where(l => l.Id == idLingua && l.Attiva).Select(l => l.Codice).SingleOrDefaultAsync(annullamento)
            : null;
        var lingua = codiceLingua ?? Lingua.CodiceItaliano;

        var adesso = Adesso();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var invito = new InvitoAccount
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = idTenant,
            IdUtenza = invitato.Id,
            HashToken = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)),
            ScadeIlUtc = adesso + _opzioniInviti.Validita,
            CreatoDaIdUtenza = attore.Id,
            CreatoIlUtc = adesso,
        };
        var url = $"{link.Schema}://{dominio}{(link.Porta is { } porta ? $":{porta}" : string.Empty)}/admin/invito?token={token}";

        try
        {
            var messaggio = await _modelli.ComponiAsync(
                idTenant,
                CodiciModelloEmail.InvitoAmministratore,
                lingua,
                invitato.Email,
                invitato.NomeVisualizzato,
                new Dictionary<string, object?>
                {
                    ["nome"] = invitato.NomeVisualizzato,
                    ["nome_tenant"] = nomeTenant,
                    ["link"] = url,
                    ["ore_validita"] = (int)_opzioniInviti.Validita.TotalHours,
                },
                annullamento);
            _email.Accoda(
                idTenant,
                messaggio,
                chiaveIdempotenza: $"invito:{invito.Id}",
                riservato: true);
        }
        catch (Exception eccezione) when (eccezione is InvalidOperationException or CryptographicException)
        {
            return CodiceEsito.ChiaveSegretiMancante;
        }

        var aperti = await _contesto.InvitiAccount.IgnoreQueryFilters()
            .Where(candidato => candidato.IdUtenza == invitato.Id && candidato.Stato == StatoInvito.Inviato)
            .ToListAsync(annullamento);
        foreach (var vecchio in aperti)
        {
            vecchio.Stato = StatoInvito.Annullato;
        }

        _contesto.InvitiAccount.Add(invito);
        return CodiceEsito.Riuscito;
    }

    private static bool TentaLink(DatiLinkInvito link) =>
        link.Schema is "http" or "https" && link.Porta is null or (> 0 and <= 65535);

    public Task<EsitoOperazione> ElevaATenantAdminAsync(Guid idAttore, Guid idTenant, Guid idUtenza, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.ElevazioneATenantAdmin, "Utenza", idTenant, async _ =>
        {
            var utenza = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(candidata => candidata.Id == idUtenza && candidata.IdTenant == idTenant, annullamento);
            if (utenza is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            if (utenza.Ruolo != RuoloUtenza.Utente || utenza.Stato != StatoUtenza.Attiva)
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            utenza.Ruolo = RuoloUtenza.TenantAdmin;
            utenza.TimbroSicurezza = Guid.NewGuid();
            utenza.AggiornatoIlUtc = Adesso();
            return Esito.Ok(utenza.Id, idTenant, $"account '{utenza.Email}'");
        }, annullamento);

    public Task<EsitoOperazione> ImpostaUtenzaAttivaAsync(Guid idAttore, Guid idTenant, Guid idUtenza, bool attiva, CancellationToken annullamento) =>
        EseguiAsync(idAttore, attiva ? AzioniAudit.RiabilitazioneUtenza : AzioniAudit.DisabilitazioneUtenza, "Utenza", idTenant, async _ =>
        {
            var utenza = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(candidata => candidata.Id == idUtenza && candidata.IdTenant == idTenant, annullamento);
            if (utenza is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            // Un acquirente che non ha ancora confermato l'email non si attiva a mano (avrebbe una password che nessuno conosce).
            // Un account anonimizzato su richiesta (A7) non torna indietro: i dati personali non esistono più.
            if (utenza.Stato is StatoUtenza.InAttesaDiConferma or StatoUtenza.Anonimizzata)
            {
                return Esito.Non(CodiceEsito.NonConsentito);
            }

            utenza.Stato = attiva ? StatoUtenza.Attiva : StatoUtenza.Disabilitata;
            utenza.TimbroSicurezza = Guid.NewGuid();
            utenza.AggiornatoIlUtc = Adesso();
            return Esito.Ok(utenza.Id, idTenant, $"account '{utenza.Email}'");
        }, annullamento);

    public Task<EsitoOperazione> ReimpostaDueFattoriAsync(Guid idAttore, Guid idTenant, Guid idUtenza, CancellationToken annullamento) =>
        EseguiAsync(idAttore, AzioniAudit.ReimpostazioneDueFattori, "Utenza", idTenant, async _ =>
        {
            // Solo i TenantAdmin del tenant indicato: il PlatformAdmin si reimposta con il comando operativo, mai da questa pagina.
            var utenza = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(
                candidata => candidata.Id == idUtenza && candidata.IdTenant == idTenant && candidata.Ruolo == RuoloUtenza.TenantAdmin,
                annullamento);
            if (utenza is null)
            {
                return Esito.Non(CodiceEsito.NonTrovato);
            }

            await AzzeramentoDueFattori.ApplicaAsync(_contesto, utenza, Adesso(), annullamento);
            return Esito.Ok(utenza.Id, idTenant, $"account '{utenza.Email}'");
        }, annullamento);

    /// <summary>
    /// Verifica l'attore, esegue l'operazione e, se riuscita, registra l'audit: audit e modifiche si salvano insieme
    /// (una sola <c>SaveChanges</c>), quindi o ci sono entrambi o nessuno dei due.
    /// </summary>
    private async Task<EsitoOperazione> EseguiAsync(
        Guid idAttore,
        string azione,
        string risorsa,
        Guid? idTenant,
        Func<Utenza, Task<Esito>> operazione,
        CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, azione, risorsa, idTenant, annullamento);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var esito = await operazione(attore);
        if (esito.Codice != CodiceEsito.Riuscito)
        {
            return new EsitoOperazione(esito.Codice);
        }

        try
        {
            await _audit.RegistraAsync(azione, risorsa, RegistroAudit.EsitoRiuscito, esito.IdTenant ?? idTenant, attore.Id, esito.Dettagli, annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, esito.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
        catch (DbUpdateException)
        {
            // Violazione di un indice univoco (host o email) per una richiesta concorrente.
            return new EsitoOperazione(CodiceEsito.GiaPresente);
        }
    }

    private async Task RichiediAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata al PlatformAdmin.");
        }
    }

    private Task<Utenza?> TrovaAttoreAsync(Guid idAttore, CancellationToken annullamento) =>
        _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore && candidata.Ruolo == RuoloUtenza.PlatformAdmin && candidata.Stato == StatoUtenza.Attiva,
            annullamento);

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione, string risorsa, Guid? idTenant, CancellationToken annullamento)
    {
        // L'attore si registra solo se è un account reale (la chiave esterna lo richiede); un tenant inesistente non si cita.
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore, annullamento);
        var tenantEsiste = idTenant is { } id && await _contesto.Tenant.IgnoreQueryFilters().AnyAsync(candidato => candidato.Id == id, annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            risorsa,
            RegistroAudit.EsitoNegato,
            tenantEsiste ? idTenant : null,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {azione}",
            annullamento);
    }

    private Task<DominioSito?> TrovaDominioAsync(Guid idTenant, Guid idDominio, CancellationToken annullamento) =>
        _contesto.DominiSito.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidato => candidato.Id == idDominio && candidato.IdTenant == idTenant,
            annullamento);

    private Task<bool> HostEsistenteAsync(string host, CancellationToken annullamento) =>
        _contesto.DominiSito.IgnoreQueryFilters().AnyAsync(candidato => candidato.Host == host, annullamento);

    private DominioSito NuovoDominio(Guid idTenant, Guid idSito, string host, bool verificato, Utenza attore)
    {
        var dominio = DominioSito.CreaVerificato(idTenant, idSito, host, soloSviluppo: false, attore.Id, _orologio);
        if (!verificato)
        {
            dominio.VerificatoIlUtc = null;
            dominio.VerificatoDaIdUtenza = null;
        }

        return dominio;
    }

    private DateTime Adesso() => _orologio.GetUtcNow().UtcDateTime;

    private static bool TentaNome(string? testo, out string nome)
    {
        nome = testo?.Trim() ?? string.Empty;
        return nome.Length is > 0 and <= LunghezzaMassimaNome && !nome.Any(char.IsControl);
    }

    private static bool TentaFusoOrario(string? testo, out string fuso)
    {
        fuso = string.IsNullOrWhiteSpace(testo) ? Sito.FusoOrarioPredefinito : testo.Trim();
        return fuso.Length <= LunghezzaMassimaFusoOrario && TimeZoneInfo.TryFindSystemTimeZoneById(fuso, out _);
    }

    /// <summary>Host digitato nel menu "Platform": un nome DNS con almeno un punto (<c>localhost</c> si registra solo col popolamento).</summary>
    private static bool TentaHostUtente(string? testo, out string host) =>
        NormalizzatoreHost.TentaNormalizza(testo, out host) && host.Contains('.', StringComparison.Ordinal);

    private sealed record Esito(CodiceEsito Codice, Guid? Id, Guid? IdTenant, string? Dettagli)
    {
        public static Esito Ok(Guid id, Guid? idTenant, string? dettagli) => new(CodiceEsito.Riuscito, id, idTenant, dettagli);

        public static Esito Non(CodiceEsito codice) => new(codice, null, null, null);
    }
}

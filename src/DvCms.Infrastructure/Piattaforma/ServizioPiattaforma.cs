using DvCms.Application.Accessi;
using DvCms.Application.Piattaforma;
using DvCms.Domain;
using DvCms.Domain.Accessi;
using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Piattaforma;

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
    private readonly TimeProvider _orologio;

    public ServizioPiattaforma(ContestoDatabase contesto, IServizioAudit audit, IHashPassword hash, TimeProvider orologio)
    {
        _contesto = contesto;
        _audit = audit;
        _hash = hash;
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
                    dominio.VerificatoDaIdUtenza is { } id && verificatori.TryGetValue(id, out var email) ? email : null))]))],
            [.. account.Select(utenza => new DettaglioAccount(
                utenza.Id,
                utenza.Email,
                utenza.NomeVisualizzato,
                utenza.Ruolo,
                utenza.Stato,
                utenza.DeveCambiarePassword,
                utenza.UltimoAccessoIlUtc))]);
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
            return Esito.Ok(sito.Id, idTenant, $"sito '{sito.Nome}', stato {sito.Stato}");
        }, annullamento);

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

            utenza.Stato = attiva ? StatoUtenza.Attiva : StatoUtenza.Disabilitata;
            utenza.TimbroSicurezza = Guid.NewGuid();
            utenza.AggiornatoIlUtc = Adesso();
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

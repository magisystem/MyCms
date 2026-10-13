using DvCms.Contracts.Accessi;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.Extender;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Localizzazione;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DvCms.Data.Popolamento;

/// <summary>Esito del popolamento: cosa è stato creato e gli avvisi su dati esistenti che divergono dalla configurazione.</summary>
public sealed record EsitoPopolamento(IReadOnlyList<string> Creati, IReadOnlyList<string> Avvisi);

/// <summary>
/// Popolamento idempotente (ADR-0014, comando <c>seed</c>): crea il tenant primario con sito, domini e profilo Guest,
/// il primo <see cref="RuoloUtenza.PlatformAdmin"/> e, solo in <c>Development</c>, i tenant di prova.
/// Crea solo ciò che manca: non sovrascrive dati esistenti né reimposta password; se i dati esistenti divergono dalla
/// configurazione lo scrive nel log. Password e segreti non compaiono mai nei log né nell'audit.
/// </summary>
public sealed partial class ServizioPopolamento
{
    public const string SezioneConfigurazione = "Seed";

    private readonly ContestoDatabase _contesto;
    private readonly IHashPassword _hash;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;
    private readonly IHostEnvironment _ambiente;
    private readonly IConfiguration _configurazione;
    private readonly ILogger<ServizioPopolamento> _logger;

    public ServizioPopolamento(
        ContestoDatabase contesto,
        IHashPassword hash,
        IServizioAudit audit,
        TimeProvider orologio,
        IHostEnvironment ambiente,
        IConfiguration configurazione,
        ILogger<ServizioPopolamento> logger)
    {
        _contesto = contesto;
        _hash = hash;
        _audit = audit;
        _orologio = orologio;
        _ambiente = ambiente;
        _configurazione = configurazione;
        _logger = logger;
    }

    /// <param name="passwordPlatformAdmin">Password fornita a mano (richiesta interattiva); se nulla si usa <c>Seed:PlatformAdmin:Password</c>.</param>
    /// <param name="annullamento">Token di annullamento.</param>
    public async Task<EsitoPopolamento> EseguiAsync(string? passwordPlatformAdmin, CancellationToken annullamento)
    {
        var creati = new List<string>();
        var avvisi = new List<string>();
        var radice = _configurazione.GetSection(SezioneConfigurazione);

        var primario = LeggiTenantPrimario(radice.GetSection("TenantPrimario"));
        var admin = LeggiPlatformAdmin(radice.GetSection("PlatformAdmin"), passwordPlatformAdmin);
        var tenantSviluppo = _ambiente.IsDevelopment() ? LeggiTenantSviluppo(radice.GetSection("TenantSviluppo")) : [];

        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);

        var tenant = await PopolaTenantPrimarioAsync(primario, creati, avvisi, annullamento);
        await PopolaPlatformAdminAsync(admin, creati, avvisi, annullamento);
        foreach (var richiesto in tenantSviluppo)
        {
            await PopolaTenantSviluppoAsync(richiesto, creati, avvisi, annullamento);
        }

        if (creati.Count > 0)
        {
            await _audit.RegistraAsync(
                AzioniAudit.Popolamento,
                "Popolamento",
                RegistroAudit.EsitoRiuscito,
                tenant.Id,
                null,
                string.Join("; ", creati),
                annullamento);
        }
        else
        {
            await _contesto.SaveChangesAsync(annullamento);
        }

        await transazione.CommitAsync(annullamento);

        foreach (var avviso in avvisi)
        {
            RegistraAvviso(_logger, avviso);
        }

        RegistraEsito(_logger, creati.Count, avvisi.Count);
        return new EsitoPopolamento(creati, avvisi);
    }

    private async Task<Tenant> PopolaTenantPrimarioAsync(
        RichiestaTenantPrimario richiesta,
        List<string> creati,
        List<string> avvisi,
        CancellationToken annullamento)
    {
        var tenant = await _contesto.Tenant.IgnoreQueryFilters().SingleOrDefaultAsync(candidato => candidato.Primario, annullamento);
        if (tenant is null)
        {
            tenant = Tenant.Crea(richiesta.Nome, primario: true, _orologio);
            _contesto.Tenant.Add(tenant);
            creati.Add($"tenant primario '{tenant.Nome}'");
        }
        else if (!string.Equals(tenant.Nome, richiesta.Nome, StringComparison.Ordinal))
        {
            avvisi.Add($"il tenant primario esistente si chiama '{tenant.Nome}', la configurazione indica '{richiesta.Nome}': non modificato");
        }

        var sito = await _contesto.Siti.IgnoreQueryFilters().OrderBy(candidato => candidato.CreatoIlUtc)
            .FirstOrDefaultAsync(candidato => candidato.IdTenant == tenant.Id, annullamento);
        if (sito is null)
        {
            sito = Sito.Crea(tenant.Id, richiesta.NomeSito, richiesta.FusoOrario, _orologio);
            _contesto.Siti.Add(sito);
            _contesto.LingueSito.Add(LinguaSito.Crea(tenant.Id, sito.Id, Lingua.IdItaliano, predefinita: true, _orologio));
            creati.Add($"sito '{sito.Nome}' del tenant primario");
        }
        else if (!string.Equals(sito.Nome, richiesta.NomeSito, StringComparison.Ordinal))
        {
            avvisi.Add($"il sito esistente del tenant primario si chiama '{sito.Nome}', la configurazione indica '{richiesta.NomeSito}': non modificato");
        }

        if (!await _contesto.ProfiliGuest.IgnoreQueryFilters().AnyAsync(candidato => candidato.IdTenant == tenant.Id, annullamento))
        {
            _contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, _orologio));
            creati.Add("profilo Guest del tenant primario");
        }

        foreach (var dominio in richiesta.Domini)
        {
            if (dominio.SoloSviluppo && !_ambiente.IsDevelopment())
            {
                avvisi.Add($"dominio '{dominio.Host}' valido solo in sviluppo: ignorato in questo ambiente");
                continue;
            }

            var esistente = await _contesto.DominiSito.IgnoreQueryFilters().SingleOrDefaultAsync(candidato => candidato.Host == dominio.Host, annullamento);
            if (esistente is null)
            {
                _contesto.DominiSito.Add(DominioSito.CreaVerificato(tenant.Id, sito.Id, dominio.Host, dominio.SoloSviluppo, null, _orologio));
                creati.Add($"dominio '{dominio.Host}'");
            }
            else if (esistente.IdTenant != tenant.Id)
            {
                avvisi.Add($"il dominio '{dominio.Host}' appartiene già a un altro tenant: non modificato");
            }
        }

        return tenant;
    }

    private async Task PopolaPlatformAdminAsync(
        RichiestaPlatformAdmin richiesta,
        List<string> creati,
        List<string> avvisi,
        CancellationToken annullamento)
    {
        var esistenti = await _contesto.Utenze.IgnoreQueryFilters()
            .Where(candidata => candidata.Ruolo == RuoloUtenza.PlatformAdmin)
            .Select(candidata => candidata.EmailNormalizzata)
            .ToListAsync(annullamento);
        if (esistenti.Count > 0)
        {
            if (!esistenti.Contains(richiesta.EmailNormalizzata))
            {
                avvisi.Add("esiste già un PlatformAdmin con un'altra email rispetto alla configurazione: non creato un secondo account");
            }

            return;
        }

        var password = richiesta.Password
            ?? throw new InvalidOperationException(
                "Password del PlatformAdmin mancante: impostare Seed:PlatformAdmin:Password con user-secrets o con la variabile d'ambiente Seed__PlatformAdmin__Password.");
        ValidaPassword(password, "del PlatformAdmin");

        _contesto.Utenze.Add(Utenza.Crea(
            RuoloUtenza.PlatformAdmin,
            null,
            richiesta.Email,
            richiesta.NomeVisualizzato,
            _hash.Calcola(password),
            deveCambiarePassword: false,
            _orologio));
        creati.Add($"PlatformAdmin '{richiesta.Email}'");
    }

    private async Task PopolaTenantSviluppoAsync(
        RichiestaTenantSviluppo richiesta,
        List<string> creati,
        List<string> avvisi,
        CancellationToken annullamento)
    {
        if (await _contesto.DominiSito.IgnoreQueryFilters().AnyAsync(candidato => candidato.Host == richiesta.Host, annullamento))
        {
            return;
        }

        var emailPlatform = await _contesto.Utenze.IgnoreQueryFilters()
            .AnyAsync(candidata => candidata.Ruolo == RuoloUtenza.PlatformAdmin && candidata.EmailNormalizzata == richiesta.EmailNormalizzata, annullamento);
        if (emailPlatform)
        {
            avvisi.Add($"tenant di sviluppo '{richiesta.Nome}' non creato: l'email del TenantAdmin coincide con quella di un PlatformAdmin");
            return;
        }

        var tenant = Tenant.Crea(richiesta.Nome, primario: false, _orologio);
        var sito = Sito.Crea(tenant.Id, $"Sito di {richiesta.Nome}", null, _orologio);
        _contesto.Tenant.Add(tenant);
        _contesto.Siti.Add(sito);
        _contesto.LingueSito.Add(LinguaSito.Crea(tenant.Id, sito.Id, Lingua.IdItaliano, predefinita: true, _orologio));
        _contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, _orologio));
        _contesto.DominiSito.Add(DominioSito.CreaVerificato(tenant.Id, sito.Id, richiesta.Host, soloSviluppo: false, null, _orologio));
        _contesto.Utenze.Add(Utenza.Crea(
            RuoloUtenza.TenantAdmin,
            tenant.Id,
            richiesta.Email,
            richiesta.NomeVisualizzato,
            _hash.Calcola(richiesta.Password),
            deveCambiarePassword: false,
            _orologio));
        creati.Add($"tenant di sviluppo '{richiesta.Nome}' ({richiesta.Host}) con TenantAdmin '{richiesta.Email}'");
    }

    private static RichiestaTenantPrimario LeggiTenantPrimario(IConfigurationSection sezione)
    {
        var nome = Obbligatorio(sezione, "Nome");
        var nomeSito = Obbligatorio(sezione.GetSection("Sito"), "Nome");
        var fuso = sezione.GetSection("Sito")["FusoOrario"];

        var domini = new List<RichiestaDominio>();
        foreach (var figlio in sezione.GetSection("Domini").GetChildren())
        {
            var host = Obbligatorio(figlio, "Host");
            if (!NormalizzatoreHost.TentaNormalizza(host, out var normalizzato))
            {
                throw new InvalidOperationException($"Host non valido in '{figlio.Path}:Host': {NormalizzatoreHost.PerMessaggio(host)}");
            }

            domini.Add(new RichiestaDominio(normalizzato, LeggiBooleano(figlio, "SoloSviluppo")));
        }

        if (domini.Count == 0)
        {
            throw new InvalidOperationException($"Configurazione '{sezione.Path}:Domini' mancante: serve almeno un dominio per il tenant primario.");
        }

        return new RichiestaTenantPrimario(nome, nomeSito, fuso, domini);
    }

    private static RichiestaPlatformAdmin LeggiPlatformAdmin(IConfigurationSection sezione, string? passwordInserita)
    {
        var email = Obbligatorio(sezione, "Email");
        if (!NormalizzatoreEmail.TentaNormalizza(email, out var normalizzata))
        {
            throw new InvalidOperationException($"Email non valida in '{sezione.Path}:Email'.");
        }

        var password = string.IsNullOrEmpty(passwordInserita) ? sezione["Password"] : passwordInserita;
        return new RichiestaPlatformAdmin(email, normalizzata, Obbligatorio(sezione, "NomeVisualizzato"), string.IsNullOrEmpty(password) ? null : password);
    }

    private static List<RichiestaTenantSviluppo> LeggiTenantSviluppo(IConfigurationSection sezione)
    {
        var elenco = new List<RichiestaTenantSviluppo>();
        foreach (var figlio in sezione.GetChildren())
        {
            var host = Obbligatorio(figlio, "Host");
            if (!NormalizzatoreHost.TentaNormalizza(host, out var normalizzato))
            {
                throw new InvalidOperationException($"Host non valido in '{figlio.Path}:Host': {NormalizzatoreHost.PerMessaggio(host)}");
            }

            var admin = figlio.GetSection("TenantAdmin");
            var email = Obbligatorio(admin, "Email");
            if (!NormalizzatoreEmail.TentaNormalizza(email, out var emailNormalizzata))
            {
                throw new InvalidOperationException($"Email non valida in '{admin.Path}:Email'.");
            }

            var password = admin["Password"];
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    $"Password mancante in '{admin.Path}:Password': impostarla con user-secrets o con una variabile d'ambiente.");
            }

            ValidaPassword(password, $"del TenantAdmin '{email}'");
            elenco.Add(new RichiestaTenantSviluppo(
                Obbligatorio(figlio, "Nome"),
                normalizzato,
                email,
                emailNormalizzata,
                Obbligatorio(admin, "NomeVisualizzato"),
                password));
        }

        return elenco;
    }

    private static string Obbligatorio(IConfigurationSection sezione, string chiave)
    {
        var valore = sezione[chiave];
        return string.IsNullOrWhiteSpace(valore)
            ? throw new InvalidOperationException($"Configurazione '{sezione.Path}:{chiave}' mancante.")
            : valore.Trim();
    }

    private static bool LeggiBooleano(IConfigurationSection sezione, string chiave)
    {
        var testo = sezione[chiave];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return false;
        }

        return bool.TryParse(testo, out var valore)
            ? valore
            : throw new InvalidOperationException($"Valore non valido per '{sezione.Path}:{chiave}': atteso true o false.");
    }

    private static void ValidaPassword(string password, string soggetto)
    {
        if (PoliticaPassword.Valida(password) is { } errore)
        {
            throw new InvalidOperationException(
                $"La password {soggetto} non rispetta la politica ({errore}): da {PoliticaPassword.LunghezzaMinima} a {PoliticaPassword.LunghezzaMassima} caratteri.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Popolamento: {Avviso}")]
    private static partial void RegistraAvviso(ILogger logger, string avviso);

    [LoggerMessage(Level = LogLevel.Information, Message = "Popolamento completato: {Creati} elementi creati, {Avvisi} avvisi")]
    private static partial void RegistraEsito(ILogger logger, int creati, int avvisi);

    private sealed record RichiestaDominio(string Host, bool SoloSviluppo);

    private sealed record RichiestaTenantPrimario(string Nome, string NomeSito, string? FusoOrario, IReadOnlyList<RichiestaDominio> Domini);

    private sealed record RichiestaPlatformAdmin(string Email, string EmailNormalizzata, string NomeVisualizzato, string? Password);

    private sealed record RichiestaTenantSviluppo(
        string Nome,
        string Host,
        string Email,
        string EmailNormalizzata,
        string NomeVisualizzato,
        string Password);
}

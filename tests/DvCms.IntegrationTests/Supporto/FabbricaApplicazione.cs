using System.Net;
using DvCms.Contracts.Tenancy;
using DvCms.Data.Tenancy;
using DvCms.DTO.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>
/// Avvia l'applicazione web per i test con una stringa di connessione esplicita e, a richiesta, altro ambiente,
/// impostazioni aggiuntive, indirizzo remoto simulato e registrazione degli host risolti.
/// </summary>
public abstract class FabbricaApplicazione : WebApplicationFactory<Program>
{
    private readonly string _connessione;
    private readonly string _ambiente;
    private readonly IReadOnlyDictionary<string, string?>? _impostazioni;
    private readonly IPAddress? _indirizzoRemoto;

    protected FabbricaApplicazione(
        string connessione,
        string ambiente = "Development",
        IReadOnlyDictionary<string, string?>? impostazioni = null,
        IPAddress? indirizzoRemoto = null)
    {
        _connessione = connessione;
        _ambiente = ambiente;
        _impostazioni = impostazioni;
        _indirizzoRemoto = indirizzoRemoto;
    }

    /// <summary>Host passati al risolutore del tenant, nell'ordine (solo se la fabbrica registra le risoluzioni).</summary>
    public RegistroRisoluzioni Risoluzioni { get; } = new();

    protected virtual void ConfiguraServizi(IServiceCollection servizi)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(_ambiente);
        builder.UseSetting("ConnectionStrings:Predefinita", _connessione);
        builder.UseSetting("Sicurezza:Password:FattoreLavoroBcrypt", "4");
        builder.UseSetting("Sicurezza:Accesso:RichiesteAlMinuto", "1000");
        // Il worker dell'outbox non parte da solo nei test: i test lo invocano direttamente.
        builder.UseSetting("Outbox:Abilitato", "false");
        builder.UseSetting("Manutenzione:Abilitata", "false");
        builder.UseSetting("Cache:IntervalloLetturaSecondi", "600");
        // La 2FA obbligatoria (predefinita in produzione) è attivata solo dai test che la verificano.
        builder.UseSetting("Sicurezza:DueFattori:Obbligo", "Nessuno");
        // La prova anti-abuso delle form pubbliche (ALTCHA) è accesa solo dai test che la verificano.
        builder.UseSetting("Altcha:Abilitato", "false");
        if (_impostazioni is not null)
        {
            foreach (var coppia in _impostazioni)
            {
                builder.UseSetting(coppia.Key, coppia.Value);
            }
        }

        builder.ConfigureTestServices(servizi =>
        {
            ConfiguraServizi(servizi);
            if (_indirizzoRemoto is not null)
            {
                servizi.AddTransient<IStartupFilter>(_ => new FiltroIndirizzoRemoto(_indirizzoRemoto));
            }
        });
    }

    protected void RegistraRisoluzioni(IServiceCollection servizi)
    {
        servizi.AddSingleton(Risoluzioni);
        servizi.AddScoped<IRisolutoreTenant>(fornitore => new RisolutoreTenantSpia(
            ActivatorUtilities.CreateInstance<RisolutoreTenantDatabase>(fornitore),
            Risoluzioni));
    }

    private sealed class FiltroIndirizzoRemoto : IStartupFilter
    {
        private readonly IPAddress _indirizzo;

        public FiltroIndirizzoRemoto(IPAddress indirizzo)
        {
            _indirizzo = indirizzo;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return costruttore =>
            {
                costruttore.Use(async (HttpContext contesto, RequestDelegate successivo) =>
                {
                    contesto.Connection.RemoteIpAddress = _indirizzo;
                    await successivo(contesto);
                });
                next(costruttore);
            };
        }
    }
}

public sealed class RegistroRisoluzioni
{
    private readonly List<string?> _host = [];

    public IReadOnlyList<string?> Host
    {
        get
        {
            lock (_host)
            {
                return [.. _host];
            }
        }
    }

    internal void Aggiungi(string? host)
    {
        lock (_host)
        {
            _host.Add(host);
        }
    }
}

internal sealed class RisolutoreTenantSpia : IRisolutoreTenant
{
    private readonly IRisolutoreTenant _interno;
    private readonly RegistroRisoluzioni _registro;

    public RisolutoreTenantSpia(IRisolutoreTenant interno, RegistroRisoluzioni registro)
    {
        _interno = interno;
        _registro = registro;
    }

    public Task<RisultatoRisoluzioneTenant> RisolviAsync(string? host, CancellationToken annullamento)
    {
        _registro.Aggiungi(host);
        return _interno.RisolviAsync(host, annullamento);
    }
}

/// <summary>Risolutore di prova: qualunque host risolve lo stesso tenant, senza database.</summary>
public sealed class RisolutoreTenantFisso : IRisolutoreTenant
{
    public static readonly Guid IdTenantProva = new("01996a00-0000-7000-8000-0000000000a1");
    public static readonly Guid IdSitoProva = new("01996a00-0000-7000-8000-0000000000a2");

    public Task<RisultatoRisoluzioneTenant> RisolviAsync(string? host, CancellationToken annullamento) =>
        Task.FromResult(RisultatoRisoluzioneTenant.Risolto(
            new DatiTenantRisolto(IdTenantProva, IdSitoProva, Guid.Empty, host ?? "prova.test", TenantPrimario: false)));
}

/// <summary>
/// Per i test che non toccano il database: la stringa di connessione è un segnaposto che non apre connessioni
/// e il tenant è sempre lo stesso. Utilizzabile come fixture di xUnit.
/// </summary>
public sealed class FabbricaApplicazioneSenzaDatabase : FabbricaApplicazione
{
    public FabbricaApplicazioneSenzaDatabase()
        : base("Server=localhost;Database=DvCmsNonUsato;Integrated Security=false;TrustServerCertificate=True")
    {
    }

    protected override void ConfiguraServizi(IServiceCollection servizi)
    {
        servizi.AddScoped<IRisolutoreTenant, RisolutoreTenantFisso>();
    }
}

/// <summary>Per i test con un database reale (o una stringa di connessione specifica, anche vuota o non valida).</summary>
public sealed class FabbricaApplicazioneConConnessione : FabbricaApplicazione
{
    private readonly bool _registraRisoluzioni;
    private readonly PayPalFinto? _payPal;
    private readonly ProviderOAuthFinto? _oauthEmail;
    private readonly NexiFinto? _nexi;

    public FabbricaApplicazioneConConnessione(
        string connessione,
        string ambiente = "Development",
        IReadOnlyDictionary<string, string?>? impostazioni = null,
        IPAddress? indirizzoRemoto = null,
        bool registraRisoluzioni = false,
        PayPalFinto? payPal = null,
        ProviderOAuthFinto? oauthEmail = null,
        NexiFinto? nexi = null)
        : base(connessione, ambiente, impostazioni, indirizzoRemoto)
    {
        _registraRisoluzioni = registraRisoluzioni;
        _payPal = payPal;
        _oauthEmail = oauthEmail;
        _nexi = nexi;
    }

    protected override void ConfiguraServizi(IServiceCollection servizi)
    {
        if (_registraRisoluzioni)
        {
            RegistraRisoluzioni(servizi);
        }

        if (_payPal is not null)
        {
            // Il client PayPal vero parla con il PayPal finto: nessuna richiesta esce dal processo.
            servizi.AddHttpClient(DvCms.Services.Negozio.Pagamenti.ClientePayPal.NomeClientHttp).ConfigurePrimaryHttpMessageHandler(() => _payPal);
        }

        if (_nexi is not null)
        {
            // Il client Nexi vero parla con il Nexi finto: nessuna richiesta esce dal processo.
            servizi.AddHttpClient(DvCms.Services.Negozio.Pagamenti.ClienteNexi.NomeClientHttp).ConfigurePrimaryHttpMessageHandler(() => _nexi);
        }

        if (_oauthEmail is not null)
        {
            // Microsoft 365 e Google finti al posto dei veri (il nome è quello del client HTTP delle email).
            servizi.AddHttpClient("email").ConfigurePrimaryHttpMessageHandler(() => _oauthEmail);
        }
    }
}

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Accessi;

/// <summary>Autenticazione a cookie dell'area admin, politiche di autorizzazione e limite di richieste sull'accesso.</summary>
public static class ConfigurazioneAccessiWeb
{
    public const string NomeCookieAdmin = ".DvCms.Admin";
    public const string NomeCookieCliente = ".DvCms.Cliente";
    public const string PercorsoCliente = "/account";
    public const string PercorsoAccessoCliente = "/account/accesso";
    public const string PercorsoAdmin = "/admin";
    public const string PercorsoAccesso = "/admin/accesso";
    public const int RichiesteAccessoAlMinutoPredefinite = 10;
    public const string ChiaveConfigurazioneChiaviCookie = "Sicurezza:ChiaviCookie:Percorso";

    public static void Aggiungi(IServiceCollection servizi, IConfiguration configurazione, IWebHostEnvironment ambiente)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);
        ArgumentNullException.ThrowIfNull(ambiente);

        // Chiavi di cookie e antiforgery: nome applicazione fisso (altrimenti dipende dal percorso di installazione e ogni istanza o
        // ogni distribuzione ha un anello diverso) e, se configurato, anello su una cartella condivisa e persistente. Senza cartella
        // le chiavi stanno nel profilo dell'utente del processo: valido per una sola istanza, ma le sessioni non sopravvivono a un
        // contenitore ricreato (l'avvio lo segnala). Le chiavi dei segreti dei tenant sono un'altra cosa (ProtettoreSegreti).
        var chiavi = servizi.AddDataProtection().SetApplicationName("DvCms.Web");
        if (configurazione[ChiaveConfigurazioneChiaviCookie] is { Length: > 0 } cartella)
        {
            chiavi.PersistKeysToFileSystem(new DirectoryInfo(cartella));
        }

        // Sicuri ovunque tranne in sviluppo, dove si lavora solo in HTTP (docs/development/local-setup.md).
        var politicaSicurezza = ambiente.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        servizi.AddSingleton<SfidaSecondoFattoreWeb>();

        servizi.AddAuthentication(Schemi.Admin)
            .AddCookie(Schemi.Admin, opzioni =>
            {
                opzioni.Cookie.Name = NomeCookieAdmin;
                opzioni.Cookie.Path = PercorsoAdmin;
                opzioni.Cookie.HttpOnly = true;
                opzioni.Cookie.SameSite = SameSiteMode.Lax;
                opzioni.Cookie.SecurePolicy = politicaSicurezza;
                opzioni.Cookie.IsEssential = true;
                opzioni.ExpireTimeSpan = TimeSpan.FromHours(8);
                opzioni.SlidingExpiration = true;
                opzioni.LoginPath = PercorsoAccesso;
                opzioni.ReturnUrlParameter = "ReturnUrl";
                opzioni.Events.OnValidatePrincipal = ValidatoreSessioneAdmin.ConvalidaAsync;
                opzioni.Events.OnRedirectToAccessDenied = contesto =>
                {
                    contesto.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddCookie(Schemi.Cliente, opzioni =>
            {
                // Cookie dell'area cliente: distinto da quello admin (nome diverso, percorso "/" perché le pagine del sito sono ovunque),
                // legato all'host, senza dominio; vale solo per gli account Utente (il validatore scarta ogni altro ruolo).
                opzioni.Cookie.Name = NomeCookieCliente;
                opzioni.Cookie.Path = "/";
                opzioni.Cookie.HttpOnly = true;
                opzioni.Cookie.SameSite = SameSiteMode.Lax;
                opzioni.Cookie.SecurePolicy = politicaSicurezza;
                opzioni.Cookie.IsEssential = true;
                opzioni.ExpireTimeSpan = TimeSpan.FromHours(8);
                opzioni.SlidingExpiration = true;
                opzioni.LoginPath = PercorsoAccessoCliente;
                opzioni.ReturnUrlParameter = "ReturnUrl";
                opzioni.Events.OnValidatePrincipal = ValidatoreSessioneCliente.ConvalidaAsync;
                opzioni.Events.OnRedirectToAccessDenied = contesto =>
                {
                    contesto.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        servizi.AddAuthorizationBuilder()
            .AddPolicy(Politiche.AmministratoreTenant, politica => politica
                .AddAuthenticationSchemes(Schemi.Admin)
                .RequireAuthenticatedUser()
                .RequireRole("TenantAdmin", "PlatformAdmin"))
            .AddPolicy(Politiche.SoloAmministratoreTenant, politica => politica
                .AddAuthenticationSchemes(Schemi.Admin)
                .RequireAuthenticatedUser()
                .RequireRole("TenantAdmin"))
            .AddPolicy(Politiche.Acquirente, politica => politica
                .AddAuthenticationSchemes(Schemi.Cliente)
                .RequireAuthenticatedUser()
                .RequireRole("Utente"))
            .AddPolicy(Politiche.AmministratorePiattaforma, politica => politica
                .AddAuthenticationSchemes(Schemi.Admin)
                .RequireAuthenticatedUser()
                .RequireRole("PlatformAdmin"));

        servizi.AddAntiforgery(opzioni =>
        {
            opzioni.Cookie.Name = ".DvCms.Antiforgery";
            opzioni.Cookie.HttpOnly = true;
            opzioni.Cookie.SameSite = SameSiteMode.Strict;
            opzioni.Cookie.SecurePolicy = politicaSicurezza;
        });

        var richiesteAlMinuto = LeggiRichiesteAlMinuto(configurazione);
        servizi.AddRateLimiter(opzioni =>
        {
            opzioni.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opzioni.AddPolicy(Politiche.LimiteAccesso, contesto =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ChiaveIndirizzoIp.Calcola(contesto.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = richiesteAlMinuto,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });
    }

    private static int LeggiRichiesteAlMinuto(IConfiguration configurazione)
    {
        var testo = configurazione["Sicurezza:Accesso:RichiesteAlMinuto"];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return RichiesteAccessoAlMinutoPredefinite;
        }

        if (!int.TryParse(testo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valore) || valore < 1)
        {
            throw new InvalidOperationException("Valore non valido per 'Sicurezza:Accesso:RichiesteAlMinuto': atteso un intero positivo.");
        }

        return valore;
    }
}

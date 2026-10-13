using DvCms.Application.Localizzazione;
using DvCms.Infrastructure;
using DvCms.Web.Accessi;
using DvCms.Web.Comandi;
using DvCms.Web.Localizzazione;
using DvCms.Web.Middleware;
using DvCms.Web.Pagamenti;
using DvCms.Web.Pubblico;
using DvCms.Web.Salute;

var costruttore = WebApplication.CreateBuilder(args);

costruttore.Services.AddRazorPages(opzioni =>
{
    // Tutta l'area /admin richiede un amministratore del tenant (o il PlatformAdmin); l'accesso è l'unica eccezione.
    opzioni.Conventions.AuthorizeFolder("/Admin", Politiche.AmministratoreTenant);
    // Canale email con consenso OAuth: l'app del tenant la gestisce solo il suo TenantAdmin, non il PlatformAdmin (ADR-0021).
    opzioni.Conventions.AuthorizeFolder("/Admin/Email", Politiche.SoloAmministratoreTenant);
    opzioni.Conventions.AllowAnonymousToPage("/Admin/Accesso");
    opzioni.Conventions.AllowAnonymousToPage("/Admin/Verifica");
    opzioni.Conventions.AllowAnonymousToFolder("/Admin/Recupero");
    opzioni.Conventions.AllowAnonymousToFolder("/Admin/Invito");
    // Area cliente (F-000): solo l'acquirente autenticato con il suo cookie; registrazione, conferma e accesso sono pubblici; l'uscita richiede la sessione (il token antiforgery è legato all'utente).
    opzioni.Conventions.AuthorizeFolder("/Account", Politiche.Acquirente);
    opzioni.Conventions.AllowAnonymousToPage("/Account/Registrazione");
    opzioni.Conventions.AllowAnonymousToPage("/Account/Conferma");
    opzioni.Conventions.AllowAnonymousToPage("/Account/Accesso");
    // Il menu "Platform" è un'area dedicata: solo il PlatformAdmin, verificato lato server a ogni richiesta.
    opzioni.Conventions.AuthorizeAreaFolder("Platform", "/", Politiche.AmministratorePiattaforma);
    opzioni.Conventions.ConfigureFilter(new FiltroCambioPasswordObbligatorio());
    opzioni.Conventions.ConfigureFilter(new FiltroDueFattoriObbligatorio());
});
ConfigurazioneAccessiWeb.Aggiungi(costruttore.Services, costruttore.Configuration, costruttore.Environment);
costruttore.Services.AggiungiInfrastruttura(costruttore.Configuration);
costruttore.Services.AddHttpContextAccessor();
costruttore.Services.AggiungiSalute();
var usaProxyAttendibili = ConfigurazioneProxy.Aggiungi(costruttore.Services, costruttore.Configuration);
costruttore.Services.AddScoped<IContestoLingua, ContestoLinguaRichiesta>();

costruttore.Services.AddHsts(opzioni =>
{
    // Sei mesi, senza sottodomini né precaricamento: ogni tenant ha i propri domini e non spetta all'applicazione impegnarli oltre.
    opzioni.MaxAge = TimeSpan.FromDays(180);
    opzioni.IncludeSubDomains = false;
    opzioni.Preload = false;
});

var applicazione = costruttore.Build();

// Comandi di servizio: eseguono un'operazione e terminano senza avviare il server.
if (args is [ComandoMigra.Nome])
{
    return await ComandoMigra.EseguiAsync(applicazione.Services);
}

if (args is [ComandoVerificaArchivio.Nome])
{
    return await ComandoVerificaArchivio.EseguiAsync(applicazione.Services);
}

if (args is [ComandoReimpostaDueFattori.Nome, var emailPlatformAdmin])
{
    return await ComandoReimpostaDueFattori.EseguiAsync(applicazione.Services, emailPlatformAdmin);
}

if (args is [ComandoSeed.Nome])
{
    return await ComandoSeed.EseguiAsync(applicazione.Services, consentiRichiestaInterattiva: true);
}

if (!applicazione.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(applicazione.Configuration[ConfigurazioneAccessiWeb.ChiaveConfigurazioneChiaviCookie]))
{
    AvvisiAvvio.ChiaviCookieNonPersistenti(applicazione.Logger);
}

if (!applicazione.Environment.IsDevelopment())
{
    var dueFattori = applicazione.Services.GetRequiredService<DvCms.Application.Accessi.DueFattori.OpzioniDueFattori>();
    if (dueFattori.Obbligo == DvCms.Application.Accessi.DueFattori.ObbligoDueFattori.Nessuno)
    {
        AvvisiAvvio.DueFattoriNonObbligatori(applicazione.Logger);
    }
    else if (string.IsNullOrWhiteSpace(applicazione.Configuration["Sicurezza:ChiaveSegreti"]))
    {
        AvvisiAvvio.ChiaveSegretiMancanteConDueFattori(applicazione.Logger);
    }
}

if (!applicazione.Environment.IsDevelopment() && !usaProxyAttendibili)
{
    AvvisiAvvio.ProxyNonConfigurato(applicazione.Logger);
}

if (usaProxyAttendibili)
{
    applicazione.UseForwardedHeaders();
}

// Popolamento all'avvio: solo se richiesto esplicitamente (default falso); in produzione va disattivato dopo il primo successo.
if (applicazione.Configuration.GetValue<bool>("Seed:EseguiAllAvvio"))
{
    var esito = await ComandoSeed.EseguiAsync(applicazione.Services, consentiRichiestaInterattiva: false);
    if (esito != 0)
    {
        return esito;
    }
}

applicazione.UseMiddleware<MiddlewareIntestazioniSicurezza>();
if (!applicazione.Environment.IsDevelopment())
{
    // HTTPS terminato dal proxy (intestazioni inoltrate dei proxy attendibili): HSTS solo fuori dallo sviluppo.
    applicazione.UseHsts();
}

applicazione.UseMiddleware<MiddlewareIdCorrelazione>();
applicazione.UseMiddleware<MiddlewareMetriche>();
// Sonde di salute: prima della risoluzione del tenant (usano l'indirizzo dell'istanza, non il dominio di un sito).
applicazione.UsaSalute();
applicazione.UseMiddleware<MiddlewareTenant>();

if (!applicazione.Environment.IsDevelopment())
{
    applicazione.UseExceptionHandler("/Errore");
}

applicazione.UseMiddleware<MiddlewareRisorseStatiche>();
applicazione.UseStaticFiles();
applicazione.UseRouting();
applicazione.UseRateLimiter();
applicazione.UseAuthentication();
applicazione.UseMiddleware<MiddlewareGuest>();
applicazione.UseMiddleware<MiddlewareLingua>();
applicazione.UseAuthorization();
applicazione.MapSeo();
applicazione.MapMedia();
applicazione.MapWebhookPayPal();
applicazione.MapNotificaNexi();
applicazione.MapRazorPages();

applicazione.Run();
return 0;

// Necessaria ai test di integrazione (WebApplicationFactory<Program>).
public partial class Program;

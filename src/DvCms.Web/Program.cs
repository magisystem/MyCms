using DvCms.Application.Localizzazione;
using DvCms.Infrastructure;
using DvCms.Web.Accessi;
using DvCms.Web.Comandi;
using DvCms.Web.Localizzazione;
using DvCms.Web.Middleware;

var costruttore = WebApplication.CreateBuilder(args);

costruttore.Services.AddRazorPages(opzioni =>
{
    // Tutta l'area /admin richiede un amministratore del tenant (o il PlatformAdmin); l'accesso è l'unica eccezione.
    opzioni.Conventions.AuthorizeFolder("/Admin", Politiche.AmministratoreTenant);
    opzioni.Conventions.AllowAnonymousToPage("/Admin/Accesso");
    opzioni.Conventions.AllowAnonymousToFolder("/Admin/Recupero");
    opzioni.Conventions.AllowAnonymousToFolder("/Admin/Invito");
    // Il menu "Platform" è un'area dedicata: solo il PlatformAdmin, verificato lato server a ogni richiesta.
    opzioni.Conventions.AuthorizeAreaFolder("Platform", "/", Politiche.AmministratorePiattaforma);
    opzioni.Conventions.ConfigureFilter(new FiltroCambioPasswordObbligatorio());
});
ConfigurazioneAccessiWeb.Aggiungi(costruttore.Services, costruttore.Configuration, costruttore.Environment);
costruttore.Services.AggiungiInfrastruttura(costruttore.Configuration);
costruttore.Services.AddHttpContextAccessor();
var usaProxyAttendibili = ConfigurazioneProxy.Aggiungi(costruttore.Services, costruttore.Configuration);
costruttore.Services.AddScoped<IContestoLingua, ContestoLinguaRichiesta>();

var applicazione = costruttore.Build();

// Comandi di servizio: eseguono un'operazione e terminano senza avviare il server.
if (args is [ComandoMigra.Nome])
{
    return await ComandoMigra.EseguiAsync(applicazione.Services);
}

if (args is [ComandoSeed.Nome])
{
    return await ComandoSeed.EseguiAsync(applicazione.Services, consentiRichiestaInterattiva: true);
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

applicazione.UseMiddleware<MiddlewareIdCorrelazione>();
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
applicazione.MapRazorPages();

applicazione.Run();
return 0;

// Necessaria ai test di integrazione (WebApplicationFactory<Program>).
public partial class Program;

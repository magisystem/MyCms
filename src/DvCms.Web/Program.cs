using DvCms.Application.Localizzazione;
using DvCms.Infrastructure;
using DvCms.Web.Comandi;
using DvCms.Web.Localizzazione;
using DvCms.Web.Middleware;

var costruttore = WebApplication.CreateBuilder(args);

costruttore.Services.AddRazorPages();
costruttore.Services.AggiungiInfrastruttura(costruttore.Configuration);
costruttore.Services.AddHttpContextAccessor();
costruttore.Services.AddScoped<IContestoLingua, ContestoLinguaRichiesta>();

var applicazione = costruttore.Build();

// Comandi di servizio: eseguono un'operazione e terminano senza avviare il server.
if (args is [ComandoMigra.Nome])
{
    return await ComandoMigra.EseguiAsync(applicazione.Services);
}

applicazione.UseMiddleware<MiddlewareIdCorrelazione>();

if (!applicazione.Environment.IsDevelopment())
{
    applicazione.UseExceptionHandler("/Errore");
}

applicazione.UseStaticFiles();
applicazione.UseRouting();
applicazione.MapRazorPages();

applicazione.Run();
return 0;

// Necessaria ai test di integrazione (WebApplicationFactory<Program>).
public partial class Program;

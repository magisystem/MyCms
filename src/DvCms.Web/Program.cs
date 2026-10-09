var costruttore = WebApplication.CreateBuilder(args);

costruttore.Services.AddRazorPages();

var applicazione = costruttore.Build();

if (!applicazione.Environment.IsDevelopment())
{
    applicazione.UseExceptionHandler("/Errore");
}

applicazione.UseStaticFiles();
applicazione.UseRouting();
applicazione.MapRazorPages();

applicazione.Run();

// Necessaria ai test di integrazione (WebApplicationFactory<Program>).
public partial class Program;

using DvCms.Application.Accessi;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Admin.Recupero;

/// <summary>Prima fase del recupero password: la risposta è la stessa per qualunque indirizzo (esistente o no).</summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
public class RichiestaModel : PageModel
{
    private readonly IServizioRecuperoPassword _recupero;

    public RichiestaModel(IServizioRecuperoPassword recupero)
    {
        _recupero = recupero;
    }

    [BindProperty]
    public string? Email { get; set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAdmin) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var riferimento = await _recupero.RichiediAsync(Email, ChiaveIndirizzoIp.Calcola(HttpContext.Connection.RemoteIpAddress), annullamento);
        CookieRecupero.Imposta(HttpContext, riferimento);
        return LocalRedirect(CookieRecupero.PercorsoCodice);
    }
}

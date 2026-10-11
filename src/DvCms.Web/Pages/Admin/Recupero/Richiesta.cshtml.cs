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

    /// <summary>Provenienza del recupero: <c>account</c> per un acquirente (torna all'accesso dell'area cliente); ogni altro valore è l'amministrazione.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Da { get; set; }

    public bool PerAccount => string.Equals(Da, "account", StringComparison.Ordinal);

    public string PercorsoAccesso => PerAccount ? "/account/accesso" : "/admin/accesso";

    public string Suffisso => PerAccount ? "?da=account" : string.Empty;

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAdmin) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var riferimento = await _recupero.RichiediAsync(Email, ChiaveIndirizzoIp.Calcola(HttpContext.Connection.RemoteIpAddress), annullamento);
        CookieRecupero.Imposta(HttpContext, riferimento);
        return LocalRedirect(CookieRecupero.PercorsoCodice + Suffisso);
    }
}

using DvCms.Contracts.Accessi;
using DvCms.Contracts.Localizzazione;
using DvCms.Web.Accessi;
using DvCms.Web.AntiAbuso;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Admin.Recupero;

/// <summary>Prima fase del recupero password: la risposta è la stessa per qualunque indirizzo (esistente o no).</summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
public class RichiestaModel : PageModel
{
    private readonly IServizioRecuperoPassword _recupero;
    private readonly IVerificaAntiAbuso _antiAbuso;
    private readonly ILocalizzatore _localizzatore;

    public RichiestaModel(IServizioRecuperoPassword recupero, IVerificaAntiAbuso antiAbuso, ILocalizzatore localizzatore)
    {
        _recupero = recupero;
        _antiAbuso = antiAbuso;
        _localizzatore = localizzatore;
    }

    [BindProperty]
    public string? Email { get; set; }

    /// <summary>Provenienza del recupero: <c>account</c> per un acquirente (torna all'accesso dell'area cliente); ogni altro valore è l'amministrazione.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Da { get; set; }

    public bool PerAccount => string.Equals(Da, "account", StringComparison.Ordinal);

    /// <summary>Vero se la form porta il widget anti-abuso (ADR-0026).</summary>
    public bool AntiAbuso => _antiAbuso.Attiva;

    public string? Errore { get; private set; }

    public string PercorsoAccesso => PerAccount ? "/account/accesso" : "/admin/accesso";

    public string Suffisso => PerAccount ? "?da=account" : string.Empty;

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAdmin) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        if (!await _antiAbuso.VerificaAsync(Request, annullamento))
        {
            Errore = _localizzatore.Traduci("Non siamo riusciti a verificare che tu non sia un programma automatico. Riprova.");
            return Page();
        }

        var riferimento = await _recupero.RichiediAsync(Email, ChiaveIndirizzoIp.Calcola(HttpContext.Connection.RemoteIpAddress), annullamento);
        CookieRecupero.Imposta(HttpContext, riferimento);
        return LocalRedirect(CookieRecupero.PercorsoCodice + Suffisso);
    }
}

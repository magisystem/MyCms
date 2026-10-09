using DvCms.Application.Accessi;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Admin.Recupero;

/// <summary>Seconda fase: il codice digitato. Esito e messaggi identici per richieste reali e non reali.</summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
public class CodiceModel : PageModel
{
    private readonly IServizioRecuperoPassword _recupero;

    public CodiceModel(IServizioRecuperoPassword recupero)
    {
        _recupero = recupero;
    }

    [BindProperty]
    public string? Codice { get; set; }

    public bool Errore { get; private set; }

    public bool LinkInviato { get; private set; }

    public IActionResult OnGet() =>
        CookieRecupero.Leggi(HttpContext) is null ? LocalRedirect(CookieRecupero.PercorsoRichiesta) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var riferimento = CookieRecupero.Leggi(HttpContext);
        if (riferimento is null)
        {
            return LocalRedirect(CookieRecupero.PercorsoRichiesta);
        }

        var esito = await _recupero.VerificaCodiceAsync(riferimento, Codice, $"{Request.Scheme}://{Request.Host}", annullamento);
        if (esito == EsitoVerificaCodice.Accettato)
        {
            CookieRecupero.Elimina(HttpContext);
            LinkInviato = true;
            return Page();
        }

        Errore = true;
        Codice = null;
        return Page();
    }
}

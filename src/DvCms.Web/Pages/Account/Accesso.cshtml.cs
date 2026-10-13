using DvCms.Contracts.Accessi;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Account;

/// <summary>Accesso degli acquirenti: solo account <c>Utente</c> del tenant dell'host, con un cookie distinto da quello dell'admin.</summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccessoModel : PageModel
{
    private readonly IServizioAccesso _accesso;
    private readonly IServizioCatalogo _negozio;
    private readonly IContestoTenant _tenant;

    public AccessoModel(IServizioAccesso accesso, IServizioCatalogo negozio, IContestoTenant tenant)
    {
        _accesso = accesso;
        _negozio = negozio;
        _tenant = tenant;
    }

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public bool Errore { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl, CancellationToken annullamento)
    {
        // L'area cliente esiste solo nei siti con il negozio attivo.
        if (!await _negozio.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        if ((await HttpContext.AuthenticateAsync(Schemi.Cliente)).Succeeded)
        {
            return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoCliente);
        }

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        if (!await _negozio.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var risultato = await _accesso.AccediComeAcquirenteAsync(Email, Password, annullamento);
        if (!risultato.EsitoPositivo || risultato.Identita is null)
        {
            // Stessa risposta per ogni causa (account inesistente, password errata, non confermato, bloccato).
            Errore = true;
            Password = null;
            return Page();
        }

        await HttpContext.SignInAsync(
            Schemi.Cliente,
            PrincipalSessione.Crea(risultato.Identita, _tenant.IdTenant, Schemi.Cliente),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

        // Solo percorsi locali, e mai dell'area admin (un acquirente non ci entra comunque, ma non si indica come meta).
        var meta = !string.IsNullOrEmpty(ReturnUrl)
            && Url.IsLocalUrl(ReturnUrl)
            && !ReturnUrl.StartsWith(ConfigurazioneAccessiWeb.PercorsoAdmin, StringComparison.OrdinalIgnoreCase)
                ? ReturnUrl
                : ConfigurazioneAccessiWeb.PercorsoCliente;
        return LocalRedirect(meta);
    }
}

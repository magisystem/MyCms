using System.Security.Claims;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Localizzazione;
using DvCms.DTO.Accessi;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Admin;

public class PasswordModel : PageModel
{
    private readonly IServizioAccesso _accesso;
    private readonly ILocalizzatore _localizzatore;

    public PasswordModel(IServizioAccesso accesso, ILocalizzatore localizzatore)
    {
        _accesso = accesso;
        _localizzatore = localizzatore;
    }

    [BindProperty]
    public string? PasswordAttuale { get; set; }

    [BindProperty]
    public string? NuovaPassword { get; set; }

    [BindProperty]
    public string? ConfermaPassword { get; set; }

    public bool Obbligatorio => PrincipalSessione.DeveCambiarePassword(User);

    public string? Messaggio { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUtenza))
        {
            return Forbid();
        }

        if (!string.Equals(NuovaPassword, ConfermaPassword, StringComparison.Ordinal))
        {
            Messaggio = _localizzatore.Traduci("La conferma non coincide con la nuova password.");
            return Page();
        }

        var esito = await _accesso.CambiaPasswordAsync(idUtenza, PasswordAttuale, NuovaPassword, annullamento);
        switch (esito)
        {
            case EsitoCambioPassword.Riuscito:
                // Il timbro di sicurezza è cambiato: questa sessione non è più valida, si esce e si accede di nuovo.
                await HttpContext.SignOutAsync(Schemi.Admin);
                return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAccesso);
            case EsitoCambioPassword.PasswordNonValida:
                Messaggio = _localizzatore.Traduci("La nuova password non è valida: deve avere da 12 a 128 caratteri ed essere diversa dall'attuale.");
                return Page();
            case EsitoCambioPassword.PasswordAttualeErrata:
                Messaggio = _localizzatore.Traduci("La password attuale non è corretta.");
                return Page();
            default:
                return Forbid();
        }
    }
}

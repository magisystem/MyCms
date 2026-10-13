using DvCms.Contracts.Accessi;
using DvCms.Contracts.Tenancy;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Admin;

[EnableRateLimiting(Politiche.LimiteAccesso)]
public class AccessoModel : PageModel
{
    private readonly IServizioAccesso _accesso;
    private readonly IContestoTenant _tenant;
    private readonly SfidaSecondoFattoreWeb _sfida;

    public AccessoModel(IServizioAccesso accesso, IContestoTenant tenant, SfidaSecondoFattoreWeb sfida)
    {
        _accesso = accesso;
        _tenant = tenant;
        _sfida = sfida;
    }

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public bool Errore { get; private set; }

    public IActionResult OnGet(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAdmin);
        }

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var risultato = await _accesso.AccediAsync(Email, Password, annullamento);
        if (risultato.Sfida is { } sfida)
        {
            // Password corretta ma serve il secondo fattore: nessuna sessione, solo una sfida a vita breve.
            _sfida.Emetti(HttpContext, sfida, _tenant.IdTenant, DestinazioneAccesso.Sicura(Url, ReturnUrl));
            return LocalRedirect(SfidaSecondoFattoreWeb.PercorsoVerifica);
        }

        if (!risultato.EsitoPositivo || risultato.Identita is null)
        {
            // Messaggio e risposta identici per ogni causa di fallimento: nessuna enumerazione di account.
            Errore = true;
            Password = null;
            return Page();
        }

        await HttpContext.SignInAsync(
            Schemi.Admin,
            PrincipalSessione.Crea(risultato.Identita, _tenant.IdTenant),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

        return LocalRedirect(DestinazioneAccesso.Sicura(Url, ReturnUrl));
    }
}

using DvCms.Application.Accessi.DueFattori;
using DvCms.Application.Tenancy;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Admin;

/// <summary>Secondo passo dell'accesso (ADR-0019): codice dell'app di autenticazione oppure codice di recupero.</summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
public class VerificaModel : PageModel
{
    private readonly IServizioDueFattori _dueFattori;
    private readonly IContestoTenant _tenant;
    private readonly SfidaSecondoFattoreWeb _sfida;

    public VerificaModel(IServizioDueFattori dueFattori, IContestoTenant tenant, SfidaSecondoFattoreWeb sfida)
    {
        _dueFattori = dueFattori;
        _tenant = tenant;
        _sfida = sfida;
    }

    [BindProperty]
    public string? Codice { get; set; }

    public bool Errore { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAdmin);
        }

        return _sfida.TentaLeggi(HttpContext, _tenant.IdTenant, out _) ? Page() : LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAccesso);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        if (!_sfida.TentaLeggi(HttpContext, _tenant.IdTenant, out var sfida))
        {
            // Sfida assente, scaduta, manomessa o di un altro host: si riparte dalla password.
            _sfida.Rimuovi(HttpContext);
            return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAccesso);
        }

        var risultato = await _dueFattori.CompletaAccessoAsync(sfida.IdUtenza, sfida.TimbroSicurezza, Codice, annullamento);
        if (!risultato.EsitoPositivo || risultato.Identita is null)
        {
            Errore = true;
            Codice = null;
            return Page();
        }

        _sfida.Rimuovi(HttpContext);
        await HttpContext.SignInAsync(
            Schemi.Admin,
            PrincipalSessione.Crea(risultato.Identita, _tenant.IdTenant),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

        return LocalRedirect(DestinazioneAccesso.Sicura(Url, sfida.Destinazione));
    }
}

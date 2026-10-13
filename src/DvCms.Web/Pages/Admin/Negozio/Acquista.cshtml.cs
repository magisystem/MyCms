using DvCms.Contracts.Accessi;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Models.Accessi;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio;

/// <summary>
/// Un amministratore che vuole comprare dal proprio negozio (ADR-0011): dalla sessione di amministrazione, già passata dalla
/// verifica a due fattori, ottiene la sessione dell'area cliente. Sul sito pubblico ha **solo capacità di acquirente** (il
/// principal ha il ruolo <c>Utente</c>) e la sessione cliente si invalida con il timbro di sicurezza e con la disabilitazione
/// dell'account, come per ogni acquirente. Non esiste un secondo percorso di accesso con la sola password.
/// </summary>
public class AcquistaModel : PaginaAdmin
{
    private readonly IServizioAccesso _accesso;
    private readonly IServizioCatalogo _catalogo;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;

    public AcquistaModel(IServizioAccesso accesso, IServizioCatalogo catalogo, IContestoTenant tenant, IServizioAudit audit, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _accesso = accesso;
        _catalogo = catalogo;
        _tenant = tenant;
        _audit = audit;
    }

    public IActionResult OnGet() => Redirect("/admin");

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        if (!PrincipalSessione.TentaLeggiSessione(User, out var idUtenza, out var timbro)
            || await _accesso.ConvalidaSessioneAsync(idUtenza, timbro, annullamento) is not { } identita
            || identita.Ruolo is not (RuoloUtenza.TenantAdmin or RuoloUtenza.PlatformAdmin))
        {
            return Forbid();
        }

        await HttpContext.SignInAsync(
            Schemi.Cliente,
            PrincipalSessione.Crea(identita with { Ruolo = RuoloUtenza.Utente }, _tenant.IdTenant, Schemi.Cliente),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        await _audit.RegistraAsync(AzioniAudit.AccessoAdminComeAcquirente, "Utenza", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, identita.IdUtenza, "sessione cliente da sessione di amministrazione", annullamento);
        return Redirect("/negozio");
    }
}

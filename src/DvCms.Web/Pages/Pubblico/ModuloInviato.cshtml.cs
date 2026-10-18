using DvCms.Contracts.Moduli;
using DvCms.Core.Moduli;
using DvCms.Web.Localizzazione;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Pubblico;

/// <summary>Conferma dopo l'invio di un modulo (ADR-0027): mostra il messaggio scelto dal tenant, nella lingua dell'invio.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ModuloInviatoModel : PageModel
{
    private readonly IServizioModuloPubblico _moduli;

    public ModuloInviatoModel(IServizioModuloPubblico moduli)
    {
        _moduli = moduli;
    }

    public string Messaggio { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, string? lingua, CancellationToken annullamento)
    {
        var modulo = await _moduli.LeggiAsync(id, lingua, annullamento);
        if (modulo is null)
        {
            return NotFound();
        }

        HttpContext.Items[ContestoLinguaRichiesta.ChiaveElemento] = modulo.Lingua;
        Messaggio = TestiModulo.Per(modulo.Definizione.Conferma, modulo.Lingua, modulo.LinguaPredefinita);
        return Page();
    }
}

using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account;

public class EsciModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect(ConfigurazioneAccessiWeb.PercorsoCliente);

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(Schemi.Cliente);
        return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAccessoCliente);
    }
}

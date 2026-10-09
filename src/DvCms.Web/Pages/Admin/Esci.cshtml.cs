using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Admin;

public class EsciModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAdmin);

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(Schemi.Admin);
        return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAccesso);
    }
}

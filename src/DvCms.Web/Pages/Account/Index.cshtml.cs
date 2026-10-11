using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account;

/// <summary>Area cliente: pagina dell'acquirente autenticato (le sezioni degli ordini arrivano con le slice successive).</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}

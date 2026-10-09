using DvCms.Application.Cms;
using DvCms.Web.Pubblico;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Pubblico;

/// <summary>
/// Sito pubblico (C-003): <c>/</c> mostra la pagina iniziale e <c>/{slug}</c> la pagina con quello slug, sul sito
/// risolto dall'host. Mostra solo la revisione pubblicata; bozze, pagine ritirate e inesistenti rispondono 404.
/// Il contenuto è testo semplice: ogni riga è codificata da Razor e non viene mai emesso come HTML.
/// </summary>
public class PaginaModel : PageModel
{
    private readonly IServizioPaginePubbliche _pagine;

    public PaginaModel(IServizioPaginePubbliche pagine)
    {
        _pagine = pagine;
    }

    public VistaPagina? Vista { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? slug, CancellationToken annullamento)
    {
        var contenuto = await _pagine.TrovaAsync(slug, annullamento);
        if (contenuto is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        Vista = new VistaPagina(contenuto.Titolo, contenuto.CodiceLingua, VistaPagina.DividiParagrafi(contenuto.Contenuto), NonIndicizzare: false, null, contenuto.IdModello, contenuto.VersioneModello, contenuto.DatiModello);
        return Page();
    }
}

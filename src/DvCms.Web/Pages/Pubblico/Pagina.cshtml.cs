using DvCms.Application.Cms;
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

    public PaginaPubblica? Contenuto { get; private set; }

    /// <summary>Paragrafi (separati da una riga vuota), ciascuno come elenco di righe.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Paragrafi { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string? slug, CancellationToken annullamento)
    {
        Contenuto = await _pagine.TrovaAsync(slug, annullamento);
        if (Contenuto is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        Paragrafi = [.. Contenuto.Contenuto
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragrafo => (IReadOnlyList<string>)[.. paragrafo.Split('\n', StringSplitOptions.TrimEntries)])];
        return Page();
    }
}

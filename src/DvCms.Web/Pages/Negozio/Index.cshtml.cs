using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Negozio;

/// <summary>Elenco pubblico dei prodotti pubblicati (F-001). 404 se il sito non ha il negozio attivo.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private readonly IServizioVetrina _vetrina;
    private readonly IServizioCatalogo _catalogo;
    private readonly IContestoLingua _lingua;

    public IndexModel(IServizioVetrina vetrina, IServizioCatalogo catalogo, IContestoLingua lingua)
    {
        _vetrina = vetrina;
        _catalogo = catalogo;
        _lingua = lingua;
    }

    public IReadOnlyList<ProdottoInVetrina> Prodotti { get; private set; } = [];

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Prodotti = await _vetrina.ElencaAsync(_lingua.CodiceLingua, annullamento);
        return Page();
    }
}

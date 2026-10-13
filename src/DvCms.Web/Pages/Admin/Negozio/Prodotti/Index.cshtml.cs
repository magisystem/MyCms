using System.Globalization;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Prodotti;

/// <summary>Elenco dei prodotti del negozio del sito (F-001). 404 se il sito non ha il negozio attivo.</summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioCatalogo _catalogo;

    public IndexModel(IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _catalogo = catalogo;
    }

    public IReadOnlyList<RiepilogoProdotto> Prodotti { get; private set; } = [];

    /// <summary>Formato degli importi nell'amministrazione (italiano: virgola decimale).</summary>
    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Prodotti = await _catalogo.ElencaAsync(IdAttore, annullamento);
        return Page();
    }
}

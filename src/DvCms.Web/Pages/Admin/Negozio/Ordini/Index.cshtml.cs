using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Ordini;

/// <summary>Ordini del sito in sola lettura (F-003): gli ultimi 200. 404 se il sito non ha il negozio attivo.</summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioOrdini _ordini;
    private readonly IServizioCatalogo _catalogo;

    public IndexModel(IServizioOrdini ordini, IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _ordini = ordini;
        _catalogo = catalogo;
    }

    public IReadOnlyList<RiepilogoOrdine> Ordini { get; private set; } = [];

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Ordini = await _ordini.ElencaAsync(IdAttore, annullamento);
        return Page();
    }

    public string Stato(DvCms.Domain.Negozio.StatoOrdine stato) => EtichetteOrdine.Stato(Localizzatore, stato);
}

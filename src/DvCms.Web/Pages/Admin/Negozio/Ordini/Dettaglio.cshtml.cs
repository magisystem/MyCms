using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Ordini;

/// <summary>Dettaglio di un ordine del sito in sola lettura (F-003). Un ordine di un altro sito o tenant dà 404.</summary>
public class DettaglioModel : PaginaAdmin
{
    private readonly IServizioOrdini _ordini;
    private readonly IServizioCatalogo _catalogo;

    public DettaglioModel(IServizioOrdini ordini, IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _ordini = ordini;
        _catalogo = catalogo;
    }

    public OrdineVista Ordine { get; private set; } = null!;

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento) || await _ordini.LeggiAsync(IdAttore, id, annullamento) is not { } ordine)
        {
            return NotFound();
        }

        Ordine = ordine;
        return Page();
    }

    public string Stato(DvCms.Domain.Negozio.StatoOrdine stato) => EtichetteOrdine.Stato(Localizzatore, stato);
}

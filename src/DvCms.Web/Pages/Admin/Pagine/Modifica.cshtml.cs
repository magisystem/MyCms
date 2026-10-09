using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Pagine;

public class ModificaModel : PaginaAdmin
{
    private readonly IServizioPagine _pagine;

    public ModificaModel(IServizioPagine pagine, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagine = pagine;
    }

    public DettaglioPagina? Pagina { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        Pagina = await _pagine.LeggiAsync(IdAttore, id, annullamento);
        return Pagina is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(
        Guid id,
        string? titolo,
        string? slug,
        string? contenuto,
        string? nota,
        bool paginaIniziale,
        string? tokenConcorrenza,
        CancellationToken annullamento)
    {
        var esito = await _pagine.SalvaAsync(IdAttore, id, new DatiModificaPagina(slug, titolo, contenuto, nota, paginaIniziale, tokenConcorrenza), annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Bozza salvata."));
    }

    public async Task<IActionResult> OnPostPubblicaAsync(Guid id, string? tokenConcorrenza, CancellationToken annullamento)
    {
        var esito = await _pagine.PubblicaAsync(IdAttore, id, tokenConcorrenza, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Pagina pubblicata."));
    }

    public async Task<IActionResult> OnPostRitiraAsync(Guid id, string? tokenConcorrenza, CancellationToken annullamento)
    {
        var esito = await _pagine.RitiraAsync(IdAttore, id, tokenConcorrenza, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Pagina ritirata dal pubblico."));
    }

    private RedirectToPageResult Concludi(Guid id, DvCms.Application.Piattaforma.EsitoOperazione esito, string messaggioRiuscito)
    {
        if (esito.Riuscito)
        {
            Messaggio = messaggioRiuscito;
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id });
    }
}

using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Pagine;

public class NuovaModel : PaginaAdmin
{
    private readonly IServizioPagine _pagine;

    public NuovaModel(IServizioPagine pagine, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagine = pagine;
    }

    [BindProperty]
    public string? Titolo { get; set; }

    [BindProperty]
    public string? Slug { get; set; }

    [BindProperty]
    public string? CodiceLingua { get; set; } = "it";

    [BindProperty]
    public string? Contenuto { get; set; }

    [BindProperty]
    public bool PaginaIniziale { get; set; }

    public IReadOnlyList<LinguaDisponibile> Lingue { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken annullamento)
    {
        Lingue = await _pagine.ElencaLingueAsync(IdAttore, annullamento);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var esito = await _pagine.CreaAsync(IdAttore, new DatiNuovaPagina(Slug, Titolo, Contenuto, CodiceLingua, PaginaIniziale), annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Bozza creata.");
            return RedirectToPage("Modifica", new { id = esito.Id });
        }

        Errore = TestoErrore(esito.Codice);
        Lingue = await _pagine.ElencaLingueAsync(IdAttore, annullamento);
        return Page();
    }
}

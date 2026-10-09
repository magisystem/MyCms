using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Areas.Platform.Pages.Testi;

public class IndexModel : PaginaPiattaforma
{
    private readonly IServizioGestioneTesti _testi;

    public IndexModel(IServizioGestioneTesti testi, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _testi = testi;
    }

    [BindProperty(SupportsGet = true)]
    public string? Ricerca { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Lingua { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool DaTradurre { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public PaginaTesti Risultato { get; private set; } = new([], 1, 1, 0);

    public async Task OnGetAsync(CancellationToken annullamento)
    {
        Risultato = await _testi.ElencaAsync(IdAttore, new FiltroTesti(Ricerca, Lingua, DaTradurre, Pagina), annullamento);
    }

    public async Task<IActionResult> OnPostModificaAsync(Guid idTesto, string? testo, CancellationToken annullamento)
    {
        var esito = await _testi.ModificaAsync(IdAttore, idTesto, testo, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Testo salvato.");
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { Ricerca, Lingua, DaTradurre, Pagina });
    }
}

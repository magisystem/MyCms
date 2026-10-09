using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;
using DvCms.Domain.Cms;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Pagine;

public class NuovaModel : PaginaAdmin
{
    private readonly IServizioPagine _pagine;
    private readonly RegistroModelliPagina _modelli;

    public NuovaModel(IServizioPagine pagine, RegistroModelliPagina modelli, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagine = pagine;
        _modelli = modelli;
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

    /// <summary>Modello scelto (parametro <c>modello</c> dell'indirizzo oppure campo del modulo).</summary>
    [BindProperty(SupportsGet = true, Name = "modello")]
    public string? IdModello { get; set; }

    public ModelloPagina Modello => _modelli.Ultimo(IdModello) ?? _modelli.Ultimo(RevisionePagina.ModelloPredefinito)!;

    public IReadOnlyList<ModelloPagina> Modelli => _modelli.Ultimi;

    public IReadOnlyDictionary<string, string> Valori { get; private set; } = new Dictionary<string, string>();

    public IReadOnlyCollection<string> CampiNonValidi { get; private set; } = [];

    public IReadOnlyList<LinguaDisponibile> Lingue { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken annullamento)
    {
        Lingue = await _pagine.ElencaLingueAsync(IdAttore, annullamento);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var esito = await _pagine.CreaAsync(IdAttore, new DatiNuovaPagina(Slug, Titolo, Contenuto, CodiceLingua, PaginaIniziale, Modello.Id, DatiDelModulo()), annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Bozza creata.");
            return RedirectToPage("Modifica", new { id = esito.Id });
        }

        Errore = TestoErrore(esito.Codice);
        Valori = DatiDelModulo();
        CampiNonValidi = esito.CampiNonValidi ?? [];
        Lingue = await _pagine.ElencaLingueAsync(IdAttore, annullamento);
        return Page();
    }

    private Dictionary<string, string> DatiDelModulo() =>
        Request.Form
            .Where(campo => campo.Key.StartsWith("m_", StringComparison.Ordinal))
            .ToDictionary(campo => campo.Key[2..], campo => campo.Value.ToString(), StringComparer.Ordinal);
}

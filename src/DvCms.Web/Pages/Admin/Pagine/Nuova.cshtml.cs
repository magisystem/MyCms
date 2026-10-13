using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;
using DvCms.Models.Cms;
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

    [BindProperty]
    public string? TitoloSeo { get; set; }

    [BindProperty]
    public string? DescrizioneSeo { get; set; }

    [BindProperty]
    public bool NonIndicizzare { get; set; }

    /// <summary>Modello scelto (parametro <c>modello</c> dell'indirizzo oppure campo del modulo).</summary>
    [BindProperty(SupportsGet = true, Name = "modello")]
    public string? IdModello { get; set; }

    public ModelloPagina Modello => _modelli.Ultimo(IdModello) ?? _modelli.Ultimo(RevisionePagina.ModelloPredefinito)!;

    public IReadOnlyList<ModelloPagina> Modelli => _modelli.Ultimi;

    public IReadOnlyDictionary<string, string> Valori { get; private set; } = new Dictionary<string, string>();

    public IReadOnlyCollection<string> CampiNonValidi { get; private set; } = [];

    public IReadOnlyList<LinguaDisponibile> Lingue { get; private set; } = [];

    /// <summary>Se presente, la nuova pagina è la traduzione di questa pagina (stesso gruppo, lingua diversa).</summary>
    [BindProperty(SupportsGet = true, Name = "traduzioneDi")]
    public Guid? TraduzioneDi { get; set; }

    public string? TitoloOrigine { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        await CaricaLingueAsync(annullamento);
        return TraduzioneDi is not null && TitoloOrigine is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var esito = await _pagine.CreaAsync(IdAttore, new DatiNuovaPagina(Slug, Titolo, Contenuto, CodiceLingua, PaginaIniziale, Modello.Id, DatiDelModulo(), TraduzioneDi, TitoloSeo, DescrizioneSeo, NonIndicizzare), annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Bozza creata.");
            return RedirectToPage("Modifica", new { id = esito.Id });
        }

        Errore = TestoErrore(esito.Codice);
        Valori = DatiDelModulo();
        CampiNonValidi = esito.CampiNonValidi ?? [];
        await CaricaLingueAsync(annullamento);
        return Page();
    }

    /// <summary>Le lingue del sito; per una traduzione solo quelle in cui la pagina d'origine non esiste ancora.</summary>
    private async Task CaricaLingueAsync(CancellationToken annullamento)
    {
        Lingue = await _pagine.ElencaLingueAsync(IdAttore, annullamento);
        if (TraduzioneDi is { } idOrigine && await _pagine.LeggiAsync(IdAttore, idOrigine, annullamento) is { } origine)
        {
            TitoloOrigine = origine.Titolo;
            Lingue = origine.LingueMancanti;
        }
    }

    private Dictionary<string, string> DatiDelModulo() =>
        Request.Form
            .Where(campo => campo.Key.StartsWith("m_", StringComparison.Ordinal))
            .ToDictionary(campo => campo.Key[2..], campo => campo.Value.ToString(), StringComparer.Ordinal);
}

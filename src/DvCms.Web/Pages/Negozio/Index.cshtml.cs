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

    public IReadOnlyList<CategoriaInVetrina> Categorie { get; private set; } = [];

    /// <summary>La categoria su cui si sta filtrando, se esiste fra quelle visibili.</summary>
    public CategoriaInVetrina? Selezionata { get; private set; }

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>Il testo cercato (al più 100 caratteri), come l'ha digitato il cliente.</summary>
    public string? Ricerca { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? categoria, string? q, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Categorie = await _vetrina.CategorieAsync(_lingua.CodiceLingua, annullamento);
        Selezionata = categoria is { } id ? Categorie.FirstOrDefault(c => c.Id == id) : null;
        if (categoria is not null && Selezionata is null)
        {
            // Una categoria che non c'è (o senza prodotti visibili) non si distingue da una inesistente.
            return NotFound();
        }

        Ricerca = string.IsNullOrWhiteSpace(q) ? null : (q.Trim().Length > 100 ? q.Trim()[..100] : q.Trim());
        Prodotti = await _vetrina.CercaAsync(_lingua.CodiceLingua, Ricerca, Selezionata?.Id, annullamento);
        return Page();
    }
}

using DvCms.Application.Cms;
using DvCms.Application.Temi;
using DvCms.Web.Localizzazione;
using DvCms.Web.Pubblico;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Pubblico;

/// <summary>
/// Sito pubblico (C-003, C-008): <c>/</c> mostra la pagina iniziale e <c>/{slug}</c> la pagina con quello slug nella lingua
/// predefinita del sito; le altre lingue abilitate hanno il prefisso (<c>/en</c>, <c>/en/{slug}</c>). Mostra solo la revisione
/// pubblicata; bozze, pagine ritirate, pagine che non esistono in quella lingua e percorsi non validi rispondono 404.
/// Il contenuto è testo semplice: ogni riga è codificata da Razor e non viene mai emesso come HTML.
/// </summary>
public class PaginaModel : PageModel
{
    private readonly IServizioPaginePubbliche _pagine;
    private readonly IRisolutoreTema _temi;

    public PaginaModel(IServizioPaginePubbliche pagine, IRisolutoreTema temi)
    {
        _pagine = pagine;
        _temi = temi;
    }

    public VistaPagina? Vista { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? primo, string? secondo, CancellationToken annullamento)
    {
        var lingue = await _pagine.LingueAsync(annullamento);
        var predefinita = lingue.FirstOrDefault(lingua => lingua.Predefinita);

        // Il primo segmento è un prefisso solo se è una lingua abilitata non predefinita; altrimenti è lo slug.
        var prefissata = lingue.FirstOrDefault(lingua => !lingua.Predefinita && string.Equals(lingua.Codice, primo, StringComparison.OrdinalIgnoreCase));
        string? slug;
        if (prefissata is not null)
        {
            slug = secondo;
        }
        else if (secondo is null)
        {
            slug = primo;
        }
        else
        {
            return NonTrovata(predefinita?.Codice);
        }

        var codice = prefissata?.Codice ?? predefinita?.Codice;
        if (codice is not null)
        {
            HttpContext.Items[ContestoLinguaRichiesta.ChiaveElemento] = codice;
        }

        var risoluzione = await _pagine.RisolviAsync(slug, prefissata?.Codice, annullamento);
        if (risoluzione.PercorsoReindirizzamento is { } percorso)
        {
            // Vecchio slug o pagina iniziale chiamata per slug: un solo salto verso il percorso canonico (C-005).
            return RedirectPermanent(percorso);
        }

        if (risoluzione.Pagina is not { } contenuto)
        {
            return NonTrovata(codice);
        }

        if (contenuto.NonIndicizzare)
        {
            Response.Headers["X-Robots-Tag"] = "noindex, follow";
        }

        var origine = $"{Request.Scheme}://{Request.Host}";
        var voci = contenuto.Traduzioni.Count > 1 && !contenuto.NonIndicizzare
            ? contenuto.Traduzioni
                .Select(t => new VoceLingua(t.CodiceLingua, t.NomeLingua, origine + t.Percorso, t.CodiceLingua == contenuto.CodiceLingua))
                .ToList()
            : (IReadOnlyList<VoceLingua>)[];
        Vista = new VistaPagina(
            contenuto.Titolo,
            contenuto.CodiceLingua,
            VistaPagina.DividiParagrafi(contenuto.Contenuto),
            contenuto.NonIndicizzare,
            null,
            contenuto.IdModello,
            contenuto.VersioneModello,
            contenuto.DatiModello,
            origine + contenuto.Percorso,
            voci,
            contenuto.Traduzioni.FirstOrDefault(t => t.Predefinita) is { } predefinitaTradotta ? origine + predefinitaTradotta.Percorso : null,
            contenuto.TitoloSeo,
            contenuto.DescrizioneSeo,
            await _temi.RisolviAsync(annullamento),
            prefissata is null ? "/" : "/" + prefissata.Codice,
            VistaPagina.LeggiDocumento(contenuto.DocumentoRenderJson));
        return Page();
    }

    private PageResult NonTrovata(string? codiceLingua)
    {
        if (codiceLingua is not null)
        {
            HttpContext.Items[ContestoLinguaRichiesta.ChiaveElemento] = codiceLingua;
        }

        Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}

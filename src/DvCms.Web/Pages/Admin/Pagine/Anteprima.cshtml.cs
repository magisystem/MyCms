using DvCms.Contracts.Cms;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Temi;
using DvCms.Core.Localizzazione;
using DvCms.Web.Pubblico;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Pagine;

/// <summary>
/// Anteprima di una bozza o di una revisione (C-004): solo per gli amministratori del tenant (sta sotto <c>/admin</c>,
/// quindi richiede la sessione admin), mai indicizzabile e mai in cache. Il contenuto è testo semplice sempre codificato;
/// quando arriverà il rich text l'anteprima passerà a un'origine isolata (docs/architecture/security.md).
/// </summary>
public class AnteprimaModel : PaginaAdmin
{
    private readonly IServizioPagine _pagine;
    private readonly IRisolutoreTema _temi;

    public AnteprimaModel(IServizioPagine pagine, IRisolutoreTema temi, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagine = pagine;
        _temi = temi;
    }

    public VistaPagina? Vista { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? revisione, CancellationToken annullamento)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var anteprima = await _pagine.AnteprimaAsync(IdAttore, id, revisione, annullamento);
        if (anteprima is null)
        {
            return NotFound();
        }

        var stato = anteprima.Pubblicata
            ? Localizzatore.Traduci("Anteprima della revisione pubblicata")
            : anteprima.UltimaBozza
                ? Localizzatore.Traduci("Anteprima della bozza: non è ancora pubblica")
                : Localizzatore.Traduci("Anteprima di una revisione precedente: non è pubblica");
        Vista = new VistaPagina(
            anteprima.Titolo,
            anteprima.CodiceLingua,
            VistaPagina.DividiParagrafi(anteprima.Contenuto),
            NonIndicizzare: true,
            $"{stato} (#{anteprima.NumeroRevisione})",
            anteprima.IdModello,
            anteprima.VersioneModello,
            anteprima.DatiModello,
            Tema: await _temi.RisolviAsync(annullamento),
            Documento: VistaPagina.LeggiDocumento(anteprima.DocumentoRenderJson));
        return Page();
    }
}

using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Media;
using DvCms.Core.Localizzazione;
using DvCms.DTO.Media;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Media;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Media;

/// <summary>Dettaglio di una risorsa: testo alternativo per lingua, visibilità, eliminazione e anteprima (anche se privata).</summary>
public class DettaglioModel : PaginaAdmin
{
    private readonly IServizioMedia _media;

    public DettaglioModel(IServizioMedia media, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _media = media;
    }

    public DettaglioMedia? Risorsa { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        Risorsa = await _media.LeggiAsync(IdAttore, id, annullamento);
        return Risorsa is null ? NotFound() : Page();
    }

    /// <summary>Il file per l'anteprima nell'admin: sta sotto <c>/admin</c> quindi richiede la sessione, e serve anche le risorse private.</summary>
    public async Task<IActionResult> OnGetFileAsync(Guid id, CancellationToken annullamento)
    {
        var contenuto = await _media.ApriPerAmministratoreAsync(IdAttore, id, annullamento);
        if (contenuto is null)
        {
            return NotFound();
        }

        DvCms.Web.Pubblico.EndpointMedia.IntestazioniSicure(Response);
        Response.Headers.CacheControl = "private, no-store";
        return File(contenuto.Flusso, contenuto.TipoContenuto);
    }

    public async Task<IActionResult> OnPostTestoAsync(Guid id, string? lingua, string? testo, CancellationToken annullamento) =>
        Concludi(id, await _media.ImpostaTestoAlternativoAsync(IdAttore, id, lingua, testo, annullamento), Localizzatore.Traduci("Testo alternativo salvato."));

    public async Task<IActionResult> OnPostVisibilitaAsync(Guid id, VisibilitaMedia visibilita, CancellationToken annullamento) =>
        Concludi(id, await _media.ImpostaVisibilitaAsync(IdAttore, id, visibilita, annullamento), Localizzatore.Traduci("Visibilità aggiornata."));

    public async Task<IActionResult> OnPostEliminaAsync(Guid id, CancellationToken annullamento)
    {
        var esito = await _media.EliminaAsync(IdAttore, id, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("File eliminato.");
            return RedirectToPage("Index");
        }

        return Concludi(id, esito, string.Empty);
    }

    private RedirectToPageResult Concludi(Guid id, DvCms.DTO.Piattaforma.EsitoOperazione esito, string messaggioRiuscito)
    {
        if (esito.Riuscito)
        {
            Messaggio = messaggioRiuscito;
        }
        else
        {
            MessaggioErrore = esito.Codice == DvCms.DTO.Piattaforma.CodiceEsito.DatiNonValidi
                ? Localizzatore.Traduci("Il testo alternativo non è valido (massimo 500 caratteri, una sola riga).")
                : TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id });
    }
}

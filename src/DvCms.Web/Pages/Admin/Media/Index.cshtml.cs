using DvCms.Application.Localizzazione;
using DvCms.Application.Media;
using DvCms.Application.Piattaforma;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Media;

/// <summary>Libreria media del sito (D-003, D-004): elenco e caricamento. Il server verifica tipo, dimensioni e contenuto del file.</summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioMedia _media;

    public IndexModel(IServizioMedia media, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _media = media;
    }

    public IReadOnlyList<RiepilogoMedia> Risorse { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        Risorse = await _media.ElencaAsync(IdAttore, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostCaricaAsync(IFormFile? file, CancellationToken annullamento)
    {
        if (file is null || file.Length == 0)
        {
            MessaggioErrore = Localizzatore.Traduci("Seleziona un file da caricare.");
            return RedirectToPage();
        }

        await using var flusso = file.OpenReadStream();
        var esito = await _media.CaricaAsync(IdAttore, new FileDaCaricare(flusso, file.FileName, file.ContentType), annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("File caricato.");
            return RedirectToPage("Dettaglio", new { id = esito.Id });
        }

        MessaggioErrore = TestoErroreCaricamento(esito);
        return RedirectToPage();
    }

    private string TestoErroreCaricamento(EsitoOperazione esito)
    {
        if (esito.Codice != CodiceEsito.DatiNonValidi)
        {
            return TestoErrore(esito.Codice);
        }

        return (esito.CampiNonValidi is { Count: > 0 } campi ? campi[0] : null) switch
        {
            "Dimensione" => Localizzatore.Traduci("Il file supera la dimensione massima consentita."),
            "Tipo" => Localizzatore.Traduci("Formato non ammesso: sono ammesse immagini JPEG, PNG, GIF e WebP, e il contenuto deve corrispondere all'estensione."),
            "Dimensioni" => Localizzatore.Traduci("L'immagine ha dimensioni troppo grandi."),
            _ => Localizzatore.Traduci("Seleziona un file non vuoto."),
        };
    }
}

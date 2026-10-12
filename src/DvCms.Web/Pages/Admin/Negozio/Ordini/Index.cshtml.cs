using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Ordini;

/// <summary>
/// Ordini del sito (F-003): gli ultimi 200 e l'esportazione CSV con filtro per date, stato e formato. 404 se il sito non ha il
/// negozio attivo. L'esportazione contiene dati personali: non si mette in cache e il servizio la registra nell'audit.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PaginaAdmin
{
    private readonly IServizioOrdini _ordini;
    private readonly IServizioCatalogo _catalogo;
    private readonly IServizioEsportazioneOrdini _esportazione;

    public IndexModel(IServizioOrdini ordini, IServizioCatalogo catalogo, IServizioEsportazioneOrdini esportazione, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _ordini = ordini;
        _catalogo = catalogo;
        _esportazione = esportazione;
    }

    public static IReadOnlyList<StatoOrdine> StatiFiltrabili { get; } = Enum.GetValues<StatoOrdine>();

    public IReadOnlyList<RiepilogoOrdine> Ordini { get; private set; } = [];

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Ordini = await _ordini.ElencaAsync(IdAttore, annullamento);
        return Page();
    }

    /// <summary>Scarica il CSV. Parametri non validi: nessun download, messaggio di errore.</summary>
    public async Task<IActionResult> OnGetEsportaAsync(string? dal, string? al, string? stato, string? formato, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var fine = DateOnly.TryParseExact(al, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataAl) ? dataAl : (DateOnly?)null;
        var inizio = DateOnly.TryParseExact(dal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataDal) ? dataDal : (DateOnly?)null;
        StatoOrdine? statoScelto = Enum.TryParse<StatoOrdine>(stato, ignoreCase: true, out var s) && Enum.IsDefined(s) ? s : null;
        var daFormato = Enum.TryParse<FormatoEsportazioneOrdini>(formato, ignoreCase: true, out var f) && Enum.IsDefined(f) ? f : FormatoEsportazioneOrdini.Ordini;
        if ((!string.IsNullOrWhiteSpace(dal) && inizio is null) || (!string.IsNullOrWhiteSpace(al) && fine is null)
            || (!string.IsNullOrWhiteSpace(stato) && statoScelto is null) || (inizio is { } a && fine is { } b && a > b))
        {
            MessaggioErrore = Localizzatore.Traduci("Le date o lo stato indicati non sono validi.");
            return RedirectToPage();
        }

        EsportazioneOrdini esportazione;
        try
        {
            esportazione = await _esportazione.EsportaAsync(IdAttore, new FiltroEsportazioneOrdini(inizio, fine, statoScelto, daFormato), annullamento);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        if (esportazione.Troncata)
        {
            Response.Headers["X-Esportazione-Troncata"] = "true";
        }

        return File(esportazione.Contenuto, "text/csv; charset=utf-8", esportazione.NomeFile);
    }

    public string Stato(DvCms.Domain.Negozio.StatoOrdine stato) => EtichetteOrdine.Stato(Localizzatore, stato);
}

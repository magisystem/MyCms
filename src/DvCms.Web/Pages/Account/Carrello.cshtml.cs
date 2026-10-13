using System.Globalization;
using System.Security.Claims;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account;

/// <summary>
/// Carrello dell'acquirente (F-002). Il modulo invia soltanto identificativo della variante e quantità: prezzi, IVA e totali che si
/// vedono sono sempre quelli calcolati dal server sul catalogo, mai valori ricevuti dal browser.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CarrelloModel : PageModel
{
    private readonly IServizioCarrello _carrello;
    private readonly IContestoLingua _lingua;
    private readonly ILocalizzatore _localizzatore;

    public CarrelloModel(IServizioCarrello carrello, IContestoLingua lingua, ILocalizzatore localizzatore)
    {
        _carrello = carrello;
        _lingua = lingua;
        _localizzatore = localizzatore;
    }

    [TempData]
    public string? Errore { get; set; }

    public CarrelloVista Vista { get; private set; } = new([], [], 0m, 0m, 0m, "EUR");

    /// <summary>Formato degli importi (italiano: virgola decimale).</summary>
    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    private Guid IdAccount => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public async Task OnGetAsync(CancellationToken annullamento) =>
        Vista = await _carrello.LeggiAsync(IdAccount, _lingua.CodiceLingua, annullamento);

    public async Task<IActionResult> OnPostAggiungiAsync(Guid idVariante, int quantita, CancellationToken annullamento) =>
        await EseguiAsync(await _carrello.AggiungiAsync(IdAccount, idVariante, quantita < 1 ? 1 : quantita, annullamento));

    public async Task<IActionResult> OnPostQuantitaAsync(Guid idVariante, int quantita, CancellationToken annullamento) =>
        await EseguiAsync(await _carrello.ImpostaQuantitaAsync(IdAccount, idVariante, quantita, annullamento));

    public async Task<IActionResult> OnPostRimuoviAsync(Guid idVariante, CancellationToken annullamento) =>
        await EseguiAsync(await _carrello.ImpostaQuantitaAsync(IdAccount, idVariante, 0, annullamento));

    public async Task<IActionResult> OnPostSvuotaAsync(CancellationToken annullamento) =>
        await EseguiAsync(await _carrello.SvuotaAsync(IdAccount, annullamento));

    private Task<IActionResult> EseguiAsync(EsitoOperazione esito)
    {
        if (!esito.Riuscito)
        {
            Errore = esito.Codice switch
            {
                CodiceEsito.NonTrovato => _localizzatore.Traduci("Questo articolo non è più disponibile."),
                CodiceEsito.DatiNonValidi => _localizzatore.Traduci("Quantità non valida: massimo 99 per articolo e 50 articoli diversi."),
                CodiceEsito.Conflitto => _localizzatore.Traduci("Il carrello è cambiato nel frattempo: riprova."),
                _ => _localizzatore.Traduci("Operazione non riuscita."),
            };
        }

        return Task.FromResult<IActionResult>(RedirectToPage());
    }
}

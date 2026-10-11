using System.Globalization;
using System.Security.Claims;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account.Ordini;

/// <summary>Dettaglio di un ordine dell'acquirente (F-003). Un ordine altrui dà 404, come uno inesistente.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DettaglioModel : PageModel
{
    private readonly IServizioOrdini _ordini;
    private readonly ILocalizzatore _localizzatore;

    public DettaglioModel(IServizioOrdini ordini, ILocalizzatore localizzatore)
    {
        _ordini = ordini;
        _localizzatore = localizzatore;
    }

    [TempData]
    public string? Messaggio { get; set; }

    [TempData]
    public string? Errore { get; set; }

    public OrdineVista Ordine { get; private set; } = null!;

    public ILocalizzatore Localizzatore => _localizzatore;

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public bool PuoAnnullare => TransizioniOrdine.AnnullabileDalCliente(Ordine.Stato);

    private Guid IdAccount => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        if (await _ordini.LeggiMioAsync(IdAccount, id, annullamento) is not { } ordine)
        {
            return NotFound();
        }

        Ordine = ordine;
        return Page();
    }

    public async Task<IActionResult> OnPostAnnullaAsync(Guid id, CancellationToken annullamento)
    {
        var esito = await _ordini.AnnullaMioAsync(IdAccount, id, annullamento);
        if (esito.Codice == CodiceEsito.NonTrovato)
        {
            return NotFound();
        }

        if (esito.Riuscito)
        {
            Messaggio = _localizzatore.Traduci("Ordine annullato.");
        }
        else
        {
            Errore = esito.Codice == CodiceEsito.NonConsentito
                ? _localizzatore.Traduci("Questo ordine non si può più annullare.")
                : _localizzatore.Traduci("Operazione non riuscita.");
        }

        return RedirectToPage(new { id });
    }
}

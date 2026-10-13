using System.Globalization;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Negozio;

/// <summary>
/// Scheda pubblica di un prodotto con le sue varianti (F-001). Il modulo «aggiungi al carrello» invia soltanto identificativo della
/// variante e quantità a <c>/account/carrello</c>: prezzi e totali restano calcolati dal server. Se l'acquirente è autenticato la
/// pagina ne assume l'identità prima di emettere il token antiforgery (che è legato all'utente), altrimenti invita ad accedere.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DettaglioModel : PageModel
{
    private readonly IServizioVetrina _vetrina;
    private readonly IContestoLingua _lingua;

    public DettaglioModel(IServizioVetrina vetrina, IContestoLingua lingua)
    {
        _vetrina = vetrina;
        _lingua = lingua;
    }

    public ProdottoPubblico Prodotto { get; private set; } = null!;

    public bool Autenticato { get; private set; }

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        if (await _vetrina.LeggiAsync(id, _lingua.CodiceLingua, annullamento) is not { } prodotto)
        {
            return NotFound();
        }

        Prodotto = prodotto;
        var autenticazione = await HttpContext.AuthenticateAsync(Schemi.Cliente);
        if (autenticazione.Succeeded && autenticazione.Principal is { } principal)
        {
            // Il token antiforgery del modulo si lega all'utente della richiesta: deve essere quello che invierà il modulo.
            HttpContext.User = principal;
            Autenticato = true;
        }

        return Page();
    }
}

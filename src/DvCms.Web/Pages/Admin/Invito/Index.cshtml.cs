using DvCms.Application.Accessi;
using DvCms.Application.Localizzazione;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Admin.Invito;

/// <summary>
/// Pagina raggiunta dal link dell'invito: l'invitato sceglie la propria password. Il token arriva nell'indirizzo ma la pagina
/// non lo restituisce nelle intestazioni (<c>Referrer-Policy: no-referrer</c>), non si mette in cache e la scelta avviene con una POST.
/// </summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private readonly IServizioInviti _inviti;
    private readonly ILocalizzatore _localizzatore;

    public IndexModel(IServizioInviti inviti, ILocalizzatore localizzatore)
    {
        _inviti = inviti;
        _localizzatore = localizzatore;
    }

    [BindProperty]
    public string? Token { get; set; }

    [BindProperty]
    public string? NuovaPassword { get; set; }

    [BindProperty]
    public string? ConfermaPassword { get; set; }

    public bool LinkValido { get; private set; }

    public bool Completato { get; private set; }

    public string? Messaggio { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? token, CancellationToken annullamento)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Token = token;
        LinkValido = await _inviti.LinkValidoAsync(token, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!string.Equals(NuovaPassword, ConfermaPassword, StringComparison.Ordinal))
        {
            LinkValido = await _inviti.LinkValidoAsync(Token, annullamento);
            Messaggio = _localizzatore.Traduci("La conferma non coincide con la password.");
            return Page();
        }

        var esito = await _inviti.AccettaAsync(Token, NuovaPassword, annullamento);
        switch (esito)
        {
            case EsitoReimpostazione.Riuscita:
                Completato = true;
                break;
            case EsitoReimpostazione.PasswordNonValida:
                LinkValido = true;
                Messaggio = _localizzatore.Traduci("La password non è valida: deve avere da 12 a 128 caratteri.");
                break;
            default:
                LinkValido = false;
                break;
        }

        if (esito != EsitoReimpostazione.PasswordNonValida)
        {
            Token = null;
        }

        return Page();
    }
}

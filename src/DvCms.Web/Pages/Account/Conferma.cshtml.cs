using DvCms.Contracts.Accessi;
using DvCms.Contracts.Localizzazione;
using DvCms.DTO.Accessi;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Account;

/// <summary>
/// Pagina raggiunta dal link di conferma: chi riceve l'email sceglie la password e attiva l'account. Il token arriva
/// nell'indirizzo ma non esce nelle intestazioni (<c>Referrer-Policy: no-referrer</c>), la pagina non si mette in cache e
/// la conferma avviene con una POST (un'anteprima del link non la consuma).
/// </summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ConfermaModel : PageModel
{
    private readonly IServizioAcquirenti _acquirenti;
    private readonly ILocalizzatore _localizzatore;

    public ConfermaModel(IServizioAcquirenti acquirenti, ILocalizzatore localizzatore)
    {
        _acquirenti = acquirenti;
        _localizzatore = localizzatore;
    }

    [BindProperty]
    public string? Token { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ConfermaPassword { get; set; }

    public bool LinkValido { get; private set; }

    public bool Completato { get; private set; }

    public string? Messaggio { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? token, CancellationToken annullamento)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!await _acquirenti.RegistrazioneAbilitataAsync(annullamento))
        {
            return NotFound();
        }

        Token = token;
        LinkValido = await _acquirenti.LinkValidoAsync(token, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!await _acquirenti.RegistrazioneAbilitataAsync(annullamento))
        {
            return NotFound();
        }

        if (!string.Equals(Password, ConfermaPassword, StringComparison.Ordinal))
        {
            LinkValido = await _acquirenti.LinkValidoAsync(Token, annullamento);
            Messaggio = _localizzatore.Traduci("La conferma non coincide con la password.");
            return Page();
        }

        var esito = await _acquirenti.ConfermaAsync(Token, Password, annullamento);
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

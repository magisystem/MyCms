using DvCms.Contracts.Accessi;
using DvCms.Contracts.Localizzazione;
using DvCms.DTO.Accessi;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Admin.Recupero;

/// <summary>
/// Terza fase: la pagina raggiunta dal link monouso. Il token arriva nell'indirizzo ma la pagina non lo restituisce
/// nelle intestazioni (<c>Referrer-Policy: no-referrer</c>), non si mette in cache e il cambio avviene con una POST.
/// </summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReimpostaModel : PageModel
{
    private readonly IServizioRecuperoPassword _recupero;
    private readonly ILocalizzatore _localizzatore;

    public ReimpostaModel(IServizioRecuperoPassword recupero, ILocalizzatore localizzatore)
    {
        _recupero = recupero;
        _localizzatore = localizzatore;
    }

    [BindProperty]
    public string? Token { get; set; }

    /// <summary>Provenienza del recupero: <c>account</c> per un acquirente (torna all'accesso dell'area cliente); ogni altro valore è l'amministrazione.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Da { get; set; }

    public bool PerAccount => string.Equals(Da, "account", StringComparison.Ordinal);

    public string PercorsoAccesso => PerAccount ? "/account/accesso" : "/admin/accesso";

    public string Suffisso => PerAccount ? "?da=account" : string.Empty;

    [BindProperty]
    public string? NuovaPassword { get; set; }

    [BindProperty]
    public string? ConfermaPassword { get; set; }

    public bool LinkValido { get; private set; }

    public bool Completata { get; private set; }

    public string? Messaggio { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? token, CancellationToken annullamento)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Token = token;
        LinkValido = await _recupero.LinkValidoAsync(token, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!string.Equals(NuovaPassword, ConfermaPassword, StringComparison.Ordinal))
        {
            LinkValido = await _recupero.LinkValidoAsync(Token, annullamento);
            Messaggio = _localizzatore.Traduci("La conferma non coincide con la nuova password.");
            return Page();
        }

        var esito = await _recupero.ReimpostaAsync(Token, NuovaPassword, annullamento);
        switch (esito)
        {
            case EsitoReimpostazione.Riuscita:
                Completata = true;
                break;
            case EsitoReimpostazione.PasswordNonValida:
                LinkValido = true;
                Messaggio = _localizzatore.Traduci("La nuova password non è valida: deve avere da 12 a 128 caratteri.");
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

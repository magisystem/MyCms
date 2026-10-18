using DvCms.Contracts.Accessi;
using DvCms.Contracts.Localizzazione;
using DvCms.DTO.Accessi;
using DvCms.DTO.Piattaforma;
using DvCms.Web.Accessi;
using DvCms.Web.AntiAbuso;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Account;

/// <summary>
/// Registrazione degli acquirenti (F-000). Esiste solo nei siti con negozio e registrazione attivi (404 altrove). La risposta è la
/// stessa per un indirizzo nuovo, in attesa o già registrato; la password si sceglie dal link inviato per email.
/// </summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RegistrazioneModel : PageModel
{
    private readonly IServizioAcquirenti _acquirenti;
    private readonly ILocalizzatore _localizzatore;
    private readonly IVerificaAntiAbuso _antiAbuso;

    public RegistrazioneModel(IServizioAcquirenti acquirenti, ILocalizzatore localizzatore, IVerificaAntiAbuso antiAbuso)
    {
        _acquirenti = acquirenti;
        _localizzatore = localizzatore;
        _antiAbuso = antiAbuso;
    }

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Nome { get; set; }

    /// <summary>Vero se la form porta il widget anti-abuso (ADR-0026).</summary>
    public bool AntiAbuso => _antiAbuso.Attiva;

    public bool Disponibile { get; private set; }

    public bool Inviata { get; private set; }

    public string? Errore { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _acquirenti.RegistrazioneAbilitataAsync(annullamento))
        {
            return NotFound();
        }

        Disponibile = await _acquirenti.RegistrazioneDisponibileAsync(annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        if (!await _acquirenti.RegistrazioneAbilitataAsync(annullamento))
        {
            return NotFound();
        }

        if (!await _antiAbuso.VerificaAsync(Request, annullamento))
        {
            Disponibile = await _acquirenti.RegistrazioneDisponibileAsync(annullamento);
            Errore = _localizzatore.Traduci("Non siamo riusciti a verificare che tu non sia un programma automatico. Riprova.");
            return Page();
        }

        var risultato = await _acquirenti.RegistraAsync(new DatiRegistrazioneAcquirente(Email, Nome), new DatiLinkInvito(Request.Scheme, Request.Host.Port), annullamento);
        switch (risultato.Esito)
        {
            case EsitoRegistrazione.Accettata:
                Disponibile = true;
                Inviata = true;
                break;
            case EsitoRegistrazione.DatiNonValidi:
                Disponibile = true;
                Errore = _localizzatore.Traduci("Controlla l'indirizzo email e il nome: non sono validi.");
                break;
            default:
                Disponibile = false;
                break;
        }

        return Page();
    }
}

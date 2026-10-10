using System.Security.Claims;
using DvCms.Application.Accessi.DueFattori;
using DvCms.Application.Localizzazione;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Accessi;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Admin;

/// <summary>Autenticazione a due fattori dell'account: configurazione, codici di recupero, disattivazione (ADR-0019).</summary>
public class SicurezzaModel : PageModel
{
    private readonly IServizioDueFattori _dueFattori;
    private readonly IContestoTenant _tenant;
    private readonly ILocalizzatore _localizzatore;

    public SicurezzaModel(IServizioDueFattori dueFattori, IContestoTenant tenant, ILocalizzatore localizzatore)
    {
        _dueFattori = dueFattori;
        _tenant = tenant;
        _localizzatore = localizzatore;
    }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? Codice { get; set; }

    public StatoDueFattori Stato { get; private set; } = new(false, false, false, 0);

    /// <summary>Mostrati una sola volta, subito dopo l'attivazione o la rigenerazione.</summary>
    public IReadOnlyList<string> CodiciRecupero { get; private set; } = [];

    public ConfigurazioneDueFattori? Configurazione { get; private set; }

    public string? QrSvg { get; private set; }

    public string? Messaggio { get; private set; }

    public string? MessaggioErrore { get; private set; }

    public bool Obbligatoria => PrincipalSessione.DeveAttivareDueFattori(User);

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await CaricaStatoAsync(annullamento))
        {
            return Forbid();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAvviaAsync(CancellationToken annullamento)
    {
        if (!TentaLeggiUtenza(out var idUtenza))
        {
            return Forbid();
        }

        var esito = await _dueFattori.AvviaConfigurazioneAsync(idUtenza, Password, Codice, annullamento);
        if (esito.Riuscito && esito.Valore is { } configurazione)
        {
            Configurazione = configurazione;
            QrSvg = GeneratoreQr.Svg(configurazione.Uri);
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Esito);
        }

        return await RispondiAsync(annullamento);
    }

    public async Task<IActionResult> OnPostConfermaAsync(CancellationToken annullamento)
    {
        if (!TentaLeggiUtenza(out var idUtenza))
        {
            return Forbid();
        }

        var esito = await _dueFattori.ConfermaAttivazioneAsync(idUtenza, Codice, annullamento);
        if (esito.Riuscito && esito.Valore is { } attivazione)
        {
            // Il timbro di sicurezza è cambiato (le altre sessioni sono chiuse): questa si rinnova con l'identità aggiornata.
            await HttpContext.SignInAsync(
                Schemi.Admin,
                PrincipalSessione.Crea(attivazione.Identita, _tenant.IdTenant),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
            CodiciRecupero = attivazione.CodiciRecupero;
            Messaggio = _localizzatore.Traduci("Autenticazione a due fattori attivata.");
        }
        else if (esito.Esito == EsitoDueFattori.NessunaConfigurazione)
        {
            MessaggioErrore = _localizzatore.Traduci("Nessuna configurazione in corso: avviane una nuova.");
        }
        else
        {
            MessaggioErrore = _localizzatore.Traduci("Codice non valido. Riprova con il codice attuale dell'app, oppure avvia di nuovo la configurazione.");
            return await RispondiAsync(annullamento);
        }

        return await RispondiAsync(annullamento, sessioneRinnovata: esito.Riuscito);
    }

    public async Task<IActionResult> OnPostRigeneraAsync(CancellationToken annullamento)
    {
        if (!TentaLeggiUtenza(out var idUtenza))
        {
            return Forbid();
        }

        var esito = await _dueFattori.RigeneraCodiciRecuperoAsync(idUtenza, Password, Codice, annullamento);
        if (esito.Riuscito && esito.Valore is { } rigenerazione)
        {
            CodiciRecupero = rigenerazione.CodiciRecupero;
            Messaggio = _localizzatore.Traduci("Nuovi codici di recupero generati: i precedenti non valgono più.");
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Esito);
        }

        return await RispondiAsync(annullamento);
    }

    public async Task<IActionResult> OnPostDisattivaAsync(CancellationToken annullamento)
    {
        if (!TentaLeggiUtenza(out var idUtenza))
        {
            return Forbid();
        }

        var esito = await _dueFattori.DisattivaAsync(idUtenza, Password, Codice, annullamento);
        if (esito == EsitoDueFattori.Riuscito)
        {
            // Il timbro è cambiato: la sessione non è più valida, si esce e si accede di nuovo.
            await HttpContext.SignOutAsync(Schemi.Admin);
            return LocalRedirect(ConfigurazioneAccessiWeb.PercorsoAccesso);
        }

        MessaggioErrore = TestoErrore(esito);
        return await RispondiAsync(annullamento);
    }

    private async Task<IActionResult> RispondiAsync(CancellationToken annullamento, bool sessioneRinnovata = false)
    {
        Password = null;
        Codice = null;
        if (!await CaricaStatoAsync(annullamento))
        {
            return sessioneRinnovata ? Page() : Forbid();
        }

        return Page();
    }

    private async Task<bool> CaricaStatoAsync(CancellationToken annullamento)
    {
        if (!TentaLeggiUtenza(out var idUtenza))
        {
            return false;
        }

        var stato = await _dueFattori.LeggiStatoAsync(idUtenza, annullamento);
        if (stato is null)
        {
            return false;
        }

        Stato = stato;
        return true;
    }

    private bool TentaLeggiUtenza(out Guid idUtenza) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUtenza);

    private string TestoErrore(EsitoDueFattori esito) => esito switch
    {
        EsitoDueFattori.RichiedeVerifica => _localizzatore.Traduci("Per continuare servono la password e un codice."),
        EsitoDueFattori.ChiaveSegretiMancante => _localizzatore.Traduci("Il server non è configurato per cifrare i segreti (chiave dei segreti mancante): contatta l'amministratore della piattaforma."),
        EsitoDueFattori.NonConsentito => _localizzatore.Traduci("La verifica in due passaggi è obbligatoria per il tuo ruolo: non si può disattivare."),
        _ => _localizzatore.Traduci("Password o codice non validi."),
    };
}

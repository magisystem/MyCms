using System.Security.Claims;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Areas.Platform;

/// <summary>Base delle pagine del menu "Platform": attore della sessione e messaggi di esito localizzati.</summary>
public abstract class PaginaPiattaforma : PageModel
{
    private readonly ILocalizzatore _localizzatore;

    protected PaginaPiattaforma(ILocalizzatore localizzatore)
    {
        _localizzatore = localizzatore;
    }

    /// <summary>Messaggio dell'ultima operazione (da <c>TempData</c>), mostrato una volta.</summary>
    [TempData]
    public string? Messaggio { get; set; }

    /// <summary>Errore dell'ultima operazione dopo un reindirizzamento (da <c>TempData</c>).</summary>
    [TempData]
    public string? MessaggioErrore { get; set; }

    /// <summary>Errore della richiesta corrente (la pagina viene mostrata di nuovo senza reindirizzare).</summary>
    public string? Errore { get; protected set; }

    public string? ErroreDaMostrare => Errore ?? MessaggioErrore;

    /// <summary>Account del <c>PlatformAdmin</c> che sta operando; il servizio ne riverifica il ruolo sul database.</summary>
    protected Guid IdAttore =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    protected string TestoErrore(CodiceEsito codice) => codice switch
    {
        CodiceEsito.NonAutorizzato => _localizzatore.Traduci("Operazione non autorizzata."),
        CodiceEsito.NonTrovato => _localizzatore.Traduci("Elemento non trovato."),
        CodiceEsito.DatiNonValidi => _localizzatore.Traduci("I dati inseriti non sono validi."),
        CodiceEsito.GiaPresente => _localizzatore.Traduci("Esiste già un elemento con questo indirizzo."),
        CodiceEsito.NonConsentito => _localizzatore.Traduci("L'operazione non è consentita in questo stato."),
        CodiceEsito.Conflitto => _localizzatore.Traduci("Il dato è stato modificato da qualcun altro: ricarica la pagina e riprova."),
        _ => _localizzatore.Traduci("Operazione non riuscita."),
    };

    protected ILocalizzatore Localizzatore => _localizzatore;
}

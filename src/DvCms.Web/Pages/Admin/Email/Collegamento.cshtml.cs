using DvCms.Application.Email;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Email;

/// <summary>
/// Ritorno dal consenso OAuth (ADR-0021). Il servizio verifica <c>state</c>, cookie, scadenza, attore e indirizzo di ritorno prima
/// di scambiare il codice. Il cookie si elimina comunque, e il codice non resta nella pagina né nei messaggi.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CollegamentoModel : PaginaAdmin
{
    private readonly IServizioConnessioneEmail _email;

    public CollegamentoModel(IServizioConnessioneEmail email, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _email = email;
    }

    public async Task<IActionResult> OnGetAsync(string? code, string? state, string? error, CancellationToken annullamento)
    {
        var statoProtetto = CookieCollegamentoEmail.Leggi(HttpContext);
        CookieCollegamentoEmail.Elimina(HttpContext);

        var urlRitorno = Url.PageLink("/Admin/Email/Collegamento") ?? string.Empty;
        var esito = await _email.CompletaCollegamentoAsync(IdAttore, urlRitorno, code, state, error, statoProtetto, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Casella collegata: le email partiranno da questo indirizzo.");
        }
        else
        {
            MessaggioErrore = esito.Codice switch
            {
                CodiceEsito.ConsensoNegato => Localizzatore.Traduci("Il consenso non è stato concesso o la richiesta è scaduta: riprova dal pulsante Collega."),
                CodiceEsito.ServizioEsternoNonRiuscito => Localizzatore.Traduci("Il provider non ha completato il collegamento: controlla identificativo e segreto del client e l'indirizzo di ritorno registrato."),
                CodiceEsito.ChiaveSegretiMancante => Localizzatore.Traduci("La chiave per cifrare i segreti non è configurata sul server."),
                _ => TestoErrore(esito.Codice),
            };
        }

        return RedirectToPage("Index");
    }
}

using System.Security.Claims;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account.Ordini;

/// <summary>
/// Ritorno del cliente dalla pagina del provider (PayPal o Nexi; <c>/account/ordini/{id}/pagamento?token=…</c>). Non conferma nulla da sé:
/// chiede al server di incassare (PayPal) o leggere l'esito (Nexi) di questo ordine, una sola volta, e mostra l'esito dell'ordine. L'identificativo che arriva
/// nell'indirizzo serve solo a ritrovare il tentativo e non vale niente senza la sessione del proprietario.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PagamentoModel : PageModel
{
    private readonly IServizioPagamenti _pagamenti;
    private readonly ILocalizzatore _localizzatore;

    public PagamentoModel(IServizioPagamenti pagamenti, ILocalizzatore localizzatore)
    {
        _pagamenti = pagamenti;
        _localizzatore = localizzatore;
    }

    [TempData]
    public string? Messaggio { get; set; }

    [TempData]
    public string? Errore { get; set; }

    private Guid IdAccount => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, string? token, CancellationToken annullamento)
    {
        var risultato = await _pagamenti.CompletaAsync(IdAccount, id, token, annullamento);
        if (risultato is RisultatoPagamento.NonTrovato or RisultatoPagamento.NonAutorizzato)
        {
            return NotFound();
        }

        var testo = Testo(risultato);
        if (risultato is RisultatoPagamento.Pagato or RisultatoPagamento.InSospeso)
        {
            Messaggio = testo;
        }
        else
        {
            Errore = testo;
        }

        return Redirect($"/account/ordini/{id}");
    }

    private string Testo(RisultatoPagamento risultato) => risultato switch
    {
        RisultatoPagamento.Pagato => _localizzatore.Traduci("Pagamento ricevuto: grazie."),
        RisultatoPagamento.InSospeso => _localizzatore.Traduci("Il pagamento è in attesa di conferma: l'ordine si aggiornerà appena arriva."),
        RisultatoPagamento.Fallito => _localizzatore.Traduci("Il pagamento non è andato a buon fine: puoi riprovare."),
        RisultatoPagamento.NonApprovato => _localizzatore.Traduci("Il pagamento non è stato approvato: nulla è stato addebitato."),
        RisultatoPagamento.OrdineChiuso => _localizzatore.Traduci("Questo ordine non è più in attesa di pagamento."),
        RisultatoPagamento.DaRimborsare => _localizzatore.Traduci("Il pagamento è stato ricevuto ma l'ordine non si può completare: contatta il negozio per il rimborso."),
        RisultatoPagamento.ErroreProvider => _localizzatore.Traduci("Il servizio di pagamento non risponde in questo momento: riprova tra poco."),
        RisultatoPagamento.NonConfigurato or RisultatoPagamento.ProduzioneBloccata => _localizzatore.Traduci("Il pagamento online non è disponibile per questo negozio."),
        _ => _localizzatore.Traduci("Operazione non riuscita."),
    };
}

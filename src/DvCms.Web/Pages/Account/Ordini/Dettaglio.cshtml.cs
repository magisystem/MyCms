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
    private readonly IServizioPagamenti _pagamenti;
    private readonly ILocalizzatore _localizzatore;

    public DettaglioModel(IServizioOrdini ordini, IServizioPagamenti pagamenti, ILocalizzatore localizzatore)
    {
        _ordini = ordini;
        _pagamenti = pagamenti;
        _localizzatore = localizzatore;
    }

    [TempData]
    public string? Messaggio { get; set; }

    [TempData]
    public string? Errore { get; set; }

    public OrdineVista Ordine { get; private set; } = null!;

    /// <summary>Dove proseguire il pagamento sulla pagina di PayPal (solo dopo l'avvio). Si mostra come collegamento: il cliente non viene reindirizzato da un modulo, perché la politica dei contenuti limita i moduli al sito stesso.</summary>
    public Uri? UrlPayPal { get; private set; }

    public bool PuoPagare => Ordine.Stato is StatoOrdine.InAttesaDiPagamento or StatoOrdine.PagamentoFallito && Ordine.TotaleLordo > 0;

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

    public async Task<IActionResult> OnPostPagaAsync(Guid id, CancellationToken annullamento)
    {
        var esito = await _pagamenti.AvviaAsync(IdAccount, id, new DatiLinkInvito(Request.Scheme, Request.Host.Port), annullamento);
        if (esito.Risultato == RisultatoPagamento.NonTrovato)
        {
            return NotFound();
        }

        if (esito.Risultato == RisultatoPagamento.Avviato)
        {
            UrlPayPal = esito.UrlApprovazione;
            Ordine = (await _ordini.LeggiMioAsync(IdAccount, id, annullamento))!;
            return Page();
        }

        Errore = TestoRisultato(esito.Risultato);
        return RedirectToPage(new { id });
    }

    public string TestoRisultato(RisultatoPagamento risultato) => risultato switch
    {
        RisultatoPagamento.Pagato => _localizzatore.Traduci("Pagamento ricevuto: grazie."),
        RisultatoPagamento.InSospeso => _localizzatore.Traduci("Il pagamento è in attesa di conferma da PayPal: l'ordine si aggiornerà appena arriva."),
        RisultatoPagamento.Fallito => _localizzatore.Traduci("Il pagamento non è andato a buon fine: puoi riprovare."),
        RisultatoPagamento.NonApprovato => _localizzatore.Traduci("Il pagamento non è stato approvato su PayPal: nulla è stato addebitato."),
        RisultatoPagamento.OrdineChiuso => _localizzatore.Traduci("Questo ordine non è più in attesa di pagamento."),
        RisultatoPagamento.DaRimborsare => _localizzatore.Traduci("Il pagamento è stato ricevuto ma l'ordine non si può completare: contatta il negozio per il rimborso."),
        RisultatoPagamento.NonConfigurato or RisultatoPagamento.ProduzioneBloccata => _localizzatore.Traduci("Il pagamento online non è disponibile per questo negozio."),
        RisultatoPagamento.ErroreProvider => _localizzatore.Traduci("PayPal non risponde in questo momento: riprova tra poco."),
        _ => _localizzatore.Traduci("Operazione non riuscita."),
    };

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

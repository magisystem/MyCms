using System.Globalization;
using System.Security.Claims;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Negozio;
using DvCms.Core.Localizzazione;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Negozio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account.Ordini;

/// <summary>Dettaglio di un ordine dell'acquirente (F-003). Un ordine altrui dà 404, come uno inesistente.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DettaglioModel : PageModel
{
    private readonly IServizioOrdini _ordini;
    private readonly IServizioPagamenti _pagamenti;
    private readonly IServizioPagamentiOffline _offline;
    private readonly ILocalizzatore _localizzatore;

    public DettaglioModel(IServizioOrdini ordini, IServizioPagamenti pagamenti, IServizioPagamentiOffline offline, ILocalizzatore localizzatore)
    {
        _ordini = ordini;
        _pagamenti = pagamenti;
        _offline = offline;
        _localizzatore = localizzatore;
    }

    [TempData]
    public string? Messaggio { get; set; }

    [TempData]
    public string? Errore { get; set; }

    public OrdineVista Ordine { get; private set; } = null!;

    /// <summary>Dove proseguire il pagamento sulla pagina del provider (solo dopo l'avvio). Si mostra come collegamento: il cliente non viene reindirizzato da un modulo, perché la politica dei contenuti limita i moduli al sito stesso.</summary>
    public Uri? UrlPagamento { get; private set; }

    /// <summary>Il provider scelto dal cliente, quando <see cref="UrlPagamento"/> c'è.</summary>
    public string ProviderAvviato { get; private set; } = NomiProviderPagamento.PayPal;

    /// <summary>I provider con cui questo sito fa pagare adesso: un pulsante ciascuno.</summary>
    public IReadOnlyList<string> ProviderDisponibili { get; private set; } = [];

    /// <summary>Coordinate del bonifico e istruzioni del contrassegno (ADR-0023), lette solo per gli ordini con pagamento manuale.</summary>
    public ImpostazioniPagamentoOffline Offline { get; private set; } = ImpostazioniPagamentoOffline.Spente;

    public bool PuoPagare => Ordine.ModalitaPagamento == ModalitaPagamento.Online && Ordine.Stato is StatoOrdine.InAttesaDiPagamento or StatoOrdine.PagamentoFallito && Ordine.TotaleLordo > 0;

    /// <summary>Il bonifico è ancora atteso: si mostrano coordinate, importo e causale.</summary>
    public bool AttendeBonifico => Ordine.ModalitaPagamento == ModalitaPagamento.Bonifico && Ordine.Stato == StatoOrdine.InAttesaDiPagamento;

    public string Causale => CausaleBonifico.Per(Ordine.Numero);

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
        ProviderDisponibili = await _pagamenti.ProviderDisponibiliAsync(annullamento);
        if (ordine.ModalitaPagamento != ModalitaPagamento.Online)
        {
            Offline = await _offline.LeggiPerClienteAsync(IdAccount, annullamento);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPagaAsync(Guid id, string? provider, CancellationToken annullamento)
    {
        // Senza una scelta esplicita (moduli di versioni precedenti) vale PayPal, come prima dell'arrivo di Nexi.
        var scelto = string.IsNullOrWhiteSpace(provider) ? NomiProviderPagamento.PayPal : provider;
        var esito = await _pagamenti.AvviaAsync(IdAccount, id, scelto, new DatiLinkInvito(Request.Scheme, Request.Host.Port), annullamento);
        if (esito.Risultato == RisultatoPagamento.NonTrovato)
        {
            return NotFound();
        }

        if (esito.Risultato == RisultatoPagamento.Avviato)
        {
            UrlPagamento = esito.UrlApprovazione;
            ProviderAvviato = scelto;
            Ordine = (await _ordini.LeggiMioAsync(IdAccount, id, annullamento))!;
            return Page();
        }

        Errore = TestoRisultato(esito.Risultato);
        return RedirectToPage(new { id });
    }

    public string TestoRisultato(RisultatoPagamento risultato) => risultato switch
    {
        RisultatoPagamento.Pagato => _localizzatore.Traduci("Pagamento ricevuto: grazie."),
        RisultatoPagamento.InSospeso => _localizzatore.Traduci("Il pagamento è in attesa di conferma: l'ordine si aggiornerà appena arriva."),
        RisultatoPagamento.Fallito => _localizzatore.Traduci("Il pagamento non è andato a buon fine: puoi riprovare."),
        RisultatoPagamento.NonApprovato => _localizzatore.Traduci("Il pagamento non è stato approvato: nulla è stato addebitato."),
        RisultatoPagamento.OrdineChiuso => _localizzatore.Traduci("Questo ordine non è più in attesa di pagamento."),
        RisultatoPagamento.DaRimborsare => _localizzatore.Traduci("Il pagamento è stato ricevuto ma l'ordine non si può completare: contatta il negozio per il rimborso."),
        RisultatoPagamento.NonConfigurato or RisultatoPagamento.ProduzioneBloccata => _localizzatore.Traduci("Il pagamento online non è disponibile per questo negozio."),
        RisultatoPagamento.ErroreProvider => _localizzatore.Traduci("Il servizio di pagamento non risponde in questo momento: riprova tra poco."),
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

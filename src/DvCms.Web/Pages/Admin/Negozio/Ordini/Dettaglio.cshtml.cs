using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Ordini;

/// <summary>Dettaglio di un ordine del sito in sola lettura (F-003). Un ordine di un altro sito o tenant dà 404.</summary>
public class DettaglioModel : PaginaAdmin
{
    private readonly IServizioOrdini _ordini;
    private readonly IServizioPagamenti _pagamenti;
    private readonly IServizioRimborsi _rimborsi;
    private readonly IServizioCatalogo _catalogo;
    private readonly IServizioDatiPersonali _dati;
    private readonly IServizioGiacenze _giacenze;

    public DettaglioModel(IServizioOrdini ordini, IServizioPagamenti pagamenti, IServizioRimborsi rimborsi, IServizioCatalogo catalogo, IServizioDatiPersonali dati, IServizioGiacenze giacenze, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _ordini = ordini;
        _pagamenti = pagamenti;
        _rimborsi = rimborsi;
        _catalogo = catalogo;
        _dati = dati;
        _giacenze = giacenze;
    }

    public OrdineVista Ordine { get; private set; } = null!;

    public IReadOnlyList<TentativoPagamentoVista> Tentativi { get; private set; } = [];

    public IReadOnlyList<RimborsoVista> Rimborsi { get; private set; } = [];

    public decimal Residuo => Ordine.TotaleLordo - Ordine.TotaleRimborsato;

    /// <summary>Incassato con Nexi: il rimborso dal pannello non c'è, si fa dal back office Nexi (ADR-0022).</summary>
    public bool PagatoConNexi => Tentativi.Any(t => t.Provider == NomiProviderPagamento.Nexi && t.Stato is DvCms.Models.Negozio.StatoTentativoPagamento.Catturato or DvCms.Models.Negozio.StatoTentativoPagamento.DaRimborsare);

    /// <summary>Pagamento manuale (bonifico, contrassegno): non passa da un provider, quindi nessun rimborso dal pannello (ADR-0023).</summary>
    public bool PagamentoManuale => Ordine.ModalitaPagamento != DvCms.Models.Negozio.ModalitaPagamento.Online;

    public bool PuoRimborsare => !PagamentoManuale && Ordine.Stato is DvCms.Models.Negozio.StatoOrdine.Pagato or DvCms.Models.Negozio.StatoOrdine.Spedito or DvCms.Models.Negozio.StatoOrdine.Ritirato && Residuo > 0 && !PagatoConNexi;

    public bool DaRimborsare => Tentativi.Any(t => t.Stato == DvCms.Models.Negozio.StatoTentativoPagamento.DaRimborsare);

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento) || await _ordini.LeggiAsync(IdAttore, id, annullamento) is not { } ordine)
        {
            return NotFound();
        }

        Ordine = ordine;
        Tentativi = await _pagamenti.ElencaTentativiAsync(IdAttore, id, annullamento);
        Rimborsi = await _rimborsi.ElencaAsync(IdAttore, id, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostEvasoAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var esito = await _ordini.SegnaEvasoAsync(IdAttore, id, annullamento);
        if (esito.Codice == CodiceEsito.NonTrovato)
        {
            return NotFound();
        }

        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Ordine aggiornato.");
        }
        else
        {
            MessaggioErrore = esito.Codice == CodiceEsito.NonConsentito
                ? Localizzatore.Traduci("Solo un ordine pagato, o in contrassegno da spedire, si può segnare come spedito o ritirato.")
                : TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostPagatoAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        return EsitoAzione(
            await _ordini.SegnaPagatoAsync(IdAttore, id, annullamento),
            id,
            Localizzatore.Traduci("Pagamento registrato: il cliente riceve la conferma."),
            Localizzatore.Traduci("Solo un ordine con bonifico ancora in attesa si può segnare come pagato."));
    }

    public async Task<IActionResult> OnPostAnnullaOrdineAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        return EsitoAzione(
            await _ordini.AnnullaAsync(IdAttore, id, annullamento),
            id,
            Localizzatore.Traduci("Ordine annullato: la merce prenotata è di nuovo disponibile."),
            Localizzatore.Traduci("Si possono annullare da qui solo gli ordini con pagamento manuale non ancora incassato."));
    }

    public async Task<IActionResult> OnPostConsegnaNonRiuscitaAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        return EsitoAzione(
            await _ordini.SegnaConsegnaNonRiuscitaAsync(IdAttore, id, annullamento),
            id,
            Localizzatore.Traduci("Ordine annullato. Quando la merce rientra, rimettila in giacenza dalla pagina delle giacenze."),
            Localizzatore.Traduci("Solo un ordine in contrassegno già spedito può risultare non consegnato."));
    }

    private IActionResult EsitoAzione(EsitoOperazione esito, Guid id, string messaggioRiuscito, string messaggioNonConsentito)
    {
        if (esito.Codice == CodiceEsito.NonTrovato)
        {
            return NotFound();
        }

        if (esito.Riuscito)
        {
            Messaggio = messaggioRiuscito;
        }
        else
        {
            MessaggioErrore = esito.Codice == CodiceEsito.NonConsentito ? messaggioNonConsentito : TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAnonimizzaAcquirenteAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var esito = await _dati.AnonimizzaAcquirenteDiOrdineAsync(IdAttore, id, annullamento);
        switch (esito.Codice)
        {
            case CodiceEsito.Riuscito:
                Messaggio = Localizzatore.Traduci("Account dell'acquirente anonimizzato: gli ordini non pagati sono stati annullati.");
                break;
            case CodiceEsito.NonAutorizzato:
                return Forbid();
            default:
                MessaggioErrore = Localizzatore.Traduci("L'account di questo acquirente non esiste più o non si può anonimizzare.");
                break;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReinserisciAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var esito = await _giacenze.ReinserisciOrdineAsync(IdAttore, id, annullamento);
        switch (esito.Codice)
        {
            case CodiceEsito.Riuscito:
                Messaggio = Localizzatore.Traduci("Articoli rimessi in giacenza.");
                break;
            case CodiceEsito.NonTrovato:
                return NotFound();
            case CodiceEsito.NonAutorizzato:
                return Forbid();
            case CodiceEsito.Conflitto:
                MessaggioErrore = Localizzatore.Traduci("Gli articoli di questo ordine sono già stati rimessi in giacenza.");
                break;
            default:
                MessaggioErrore = Localizzatore.Traduci("Si possono rimettere in giacenza solo gli ordini interamente rimborsati.");
                break;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRimborsaAsync(Guid id, string? importo, string? motivo, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        decimal? valore = null;
        if (!string.IsNullOrWhiteSpace(importo))
        {
            if (!decimal.TryParse(importo.Trim().Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var letto))
            {
                MessaggioErrore = TestoRimborso(RisultatoRimborso.DatiNonValidi);
                return RedirectToPage(new { id });
            }

            valore = letto;
        }

        return EsitoRimborso(await _rimborsi.RimborsaOrdineAsync(IdAttore, id, valore, motivo, annullamento), id);
    }

    public async Task<IActionResult> OnPostRimborsaIncassoAsync(Guid id, Guid idTentativo, string? motivo, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        return EsitoRimborso(await _rimborsi.RimborsaIncassoAsync(IdAttore, idTentativo, motivo, annullamento), id);
    }

    public async Task<IActionResult> OnPostVerificaRimborsoAsync(Guid id, Guid idRimborso, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        return EsitoRimborso(await _rimborsi.VerificaAsync(IdAttore, idRimborso, annullamento), id);
    }

    public string StatoRimborso(DvCms.Models.Negozio.StatoRimborso stato) => stato switch
    {
        DvCms.Models.Negozio.StatoRimborso.InCorso => Localizzatore.Traduci("In corso"),
        DvCms.Models.Negozio.StatoRimborso.Completato => Localizzatore.Traduci("Completato"),
        DvCms.Models.Negozio.StatoRimborso.Fallito => Localizzatore.Traduci("Rifiutato"),
        _ => stato.ToString(),
    };

    private IActionResult EsitoRimborso(RisultatoRimborso risultato, Guid id)
    {
        if (risultato == RisultatoRimborso.NonTrovato)
        {
            return NotFound();
        }

        if (risultato is RisultatoRimborso.Completato or RisultatoRimborso.InCorso)
        {
            Messaggio = TestoRimborso(risultato);
        }
        else
        {
            MessaggioErrore = TestoRimborso(risultato);
        }

        return RedirectToPage(new { id });
    }

    private string TestoRimborso(RisultatoRimborso risultato) => risultato switch
    {
        RisultatoRimborso.Completato => Localizzatore.Traduci("Rimborso eseguito."),
        RisultatoRimborso.InCorso => Localizzatore.Traduci("Rimborso richiesto: PayPal lo sta elaborando, puoi verificarne l'esito più tardi."),
        RisultatoRimborso.Rifiutato => Localizzatore.Traduci("PayPal ha rifiutato il rimborso."),
        RisultatoRimborso.DatiNonValidi => Localizzatore.Traduci("Importo o motivo non validi: l'importo non può superare quanto resta da rimborsare."),
        RisultatoRimborso.NonRimborsabile => Localizzatore.Traduci("Questo ordine o incasso non si può rimborsare."),
        RisultatoRimborso.NonConfigurato => Localizzatore.Traduci("Le credenziali di pagamento del sito non sono complete."),
        RisultatoRimborso.ErroreProvider => Localizzatore.Traduci("PayPal non ha risposto: l'esito del rimborso va verificato."),
        _ => Localizzatore.Traduci("Operazione non riuscita."),
    };

    public string StatoTentativo(DvCms.Models.Negozio.StatoTentativoPagamento stato) => stato switch
    {
        DvCms.Models.Negozio.StatoTentativoPagamento.Creato => Localizzatore.Traduci("In attesa del cliente"),
        DvCms.Models.Negozio.StatoTentativoPagamento.InSospeso => Localizzatore.Traduci("Incasso in sospeso"),
        DvCms.Models.Negozio.StatoTentativoPagamento.Catturato => Localizzatore.Traduci("Incassato"),
        DvCms.Models.Negozio.StatoTentativoPagamento.Fallito => Localizzatore.Traduci("Rifiutato"),
        DvCms.Models.Negozio.StatoTentativoPagamento.Annullato => Localizzatore.Traduci("Non utilizzato"),
        DvCms.Models.Negozio.StatoTentativoPagamento.DaRimborsare => Localizzatore.Traduci("Incassato: da rimborsare"),
        DvCms.Models.Negozio.StatoTentativoPagamento.RimborsoInCorso => Localizzatore.Traduci("Rimborso in corso"),
        DvCms.Models.Negozio.StatoTentativoPagamento.Rimborsato => Localizzatore.Traduci("Rimborsato"),
        _ => stato.ToString(),
    };

    public string Modalita(DvCms.Models.Negozio.ModalitaPagamento modalita) => EtichetteOrdine.Modalita(Localizzatore, modalita);

    public string Stato(DvCms.Models.Negozio.StatoOrdine stato) => EtichetteOrdine.Stato(Localizzatore, stato);
}

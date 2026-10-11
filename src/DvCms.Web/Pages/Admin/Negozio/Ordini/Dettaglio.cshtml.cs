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

    public DettaglioModel(IServizioOrdini ordini, IServizioPagamenti pagamenti, IServizioRimborsi rimborsi, IServizioCatalogo catalogo, IServizioDatiPersonali dati, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _ordini = ordini;
        _pagamenti = pagamenti;
        _rimborsi = rimborsi;
        _catalogo = catalogo;
        _dati = dati;
    }

    public OrdineVista Ordine { get; private set; } = null!;

    public IReadOnlyList<TentativoPagamentoVista> Tentativi { get; private set; } = [];

    public IReadOnlyList<RimborsoVista> Rimborsi { get; private set; } = [];

    public decimal Residuo => Ordine.TotaleLordo - Ordine.TotaleRimborsato;

    public bool PuoRimborsare => Ordine.Stato is DvCms.Domain.Negozio.StatoOrdine.Pagato or DvCms.Domain.Negozio.StatoOrdine.Spedito or DvCms.Domain.Negozio.StatoOrdine.Ritirato && Residuo > 0;

    public bool DaRimborsare => Tentativi.Any(t => t.Stato == DvCms.Domain.Negozio.StatoTentativoPagamento.DaRimborsare);

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
                ? Localizzatore.Traduci("Solo un ordine pagato si può segnare come spedito o ritirato.")
                : TestoErrore(esito.Codice);
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

    public string StatoRimborso(DvCms.Domain.Negozio.StatoRimborso stato) => stato switch
    {
        DvCms.Domain.Negozio.StatoRimborso.InCorso => Localizzatore.Traduci("In corso"),
        DvCms.Domain.Negozio.StatoRimborso.Completato => Localizzatore.Traduci("Completato"),
        DvCms.Domain.Negozio.StatoRimborso.Fallito => Localizzatore.Traduci("Rifiutato"),
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

    public string StatoTentativo(DvCms.Domain.Negozio.StatoTentativoPagamento stato) => stato switch
    {
        DvCms.Domain.Negozio.StatoTentativoPagamento.Creato => Localizzatore.Traduci("In attesa del cliente"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.InSospeso => Localizzatore.Traduci("Incasso in sospeso"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.Catturato => Localizzatore.Traduci("Incassato"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.Fallito => Localizzatore.Traduci("Rifiutato"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.Annullato => Localizzatore.Traduci("Non utilizzato"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.DaRimborsare => Localizzatore.Traduci("Incassato: da rimborsare"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.RimborsoInCorso => Localizzatore.Traduci("Rimborso in corso"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.Rimborsato => Localizzatore.Traduci("Rimborsato"),
        _ => stato.ToString(),
    };

    public string Stato(DvCms.Domain.Negozio.StatoOrdine stato) => EtichetteOrdine.Stato(Localizzatore, stato);
}

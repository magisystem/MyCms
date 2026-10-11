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
    private readonly IServizioCatalogo _catalogo;

    public DettaglioModel(IServizioOrdini ordini, IServizioPagamenti pagamenti, IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _ordini = ordini;
        _pagamenti = pagamenti;
        _catalogo = catalogo;
    }

    public OrdineVista Ordine { get; private set; } = null!;

    public IReadOnlyList<TentativoPagamentoVista> Tentativi { get; private set; } = [];

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

    public string StatoTentativo(DvCms.Domain.Negozio.StatoTentativoPagamento stato) => stato switch
    {
        DvCms.Domain.Negozio.StatoTentativoPagamento.Creato => Localizzatore.Traduci("In attesa del cliente"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.InSospeso => Localizzatore.Traduci("Incasso in sospeso"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.Catturato => Localizzatore.Traduci("Incassato"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.Fallito => Localizzatore.Traduci("Rifiutato"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.Annullato => Localizzatore.Traduci("Non utilizzato"),
        DvCms.Domain.Negozio.StatoTentativoPagamento.DaRimborsare => Localizzatore.Traduci("Incassato: da rimborsare"),
        _ => stato.ToString(),
    };

    public string Stato(DvCms.Domain.Negozio.StatoOrdine stato) => EtichetteOrdine.Stato(Localizzatore, stato);
}

using System.Globalization;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Negozio;
using DvCms.Core.Localizzazione;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Giacenze;

/// <summary>
/// Giacenza delle varianti del sito (F-005, ADR-0020). Si imposta la quantità fisica; la parte riservata dipende dagli ordini in
/// attesa di pagamento e non si tocca da qui. 404 se il sito non ha il negozio attivo.
/// </summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioGiacenze _giacenze;
    private readonly IServizioCatalogo _catalogo;

    public IndexModel(IServizioGiacenze giacenze, IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _giacenze = giacenze;
        _catalogo = catalogo;
    }

    public IReadOnlyList<GiacenzaVista> Varianti { get; private set; } = [];

    /// <summary>Variante e testo del modulo da rimostrare dopo un errore.</summary>
    public Guid? VarianteErrata { get; private set; }

    public string? TestoErrato { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Varianti = await _giacenze.ElencaAsync(IdAttore, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostImpostaAsync(Guid idVariante, string? disponibile, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        if (!int.TryParse(disponibile?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var quantita))
        {
            return await RimostraAsync(idVariante, disponibile, CodiceEsito.DatiNonValidi, annullamento);
        }

        var esito = await _giacenze.ImpostaAsync(IdAttore, idVariante, quantita, annullamento);
        return await EsitoAsync(esito, idVariante, disponibile, Localizzatore.Traduci("Giacenza aggiornata."), annullamento);
    }

    public async Task<IActionResult> OnPostNonGestireAsync(Guid idVariante, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var esito = await _giacenze.ImpostaAsync(IdAttore, idVariante, null, annullamento);
        return await EsitoAsync(esito, idVariante, null, Localizzatore.Traduci("La giacenza non è più gestita: la variante si vende senza limite."), annullamento);
    }

    private async Task<IActionResult> EsitoAsync(EsitoOperazione esito, Guid idVariante, string? testo, string messaggio, CancellationToken annullamento)
    {
        if (esito.Riuscito)
        {
            Messaggio = messaggio;
            return RedirectToPage();
        }

        return await RimostraAsync(idVariante, testo, esito.Codice, annullamento);
    }

    private async Task<IActionResult> RimostraAsync(Guid idVariante, string? testo, CodiceEsito codice, CancellationToken annullamento)
    {
        Varianti = await _giacenze.ElencaAsync(IdAttore, annullamento);
        VarianteErrata = idVariante;
        TestoErrato = testo;
        Errore = codice switch
        {
            CodiceEsito.DatiNonValidi => Localizzatore.Traduci("Quantità non valida: un numero intero da 0 a 1.000.000, non inferiore a quanto è già riservato."),
            CodiceEsito.NonConsentito => Localizzatore.Traduci("Ci sono ordini in attesa di pagamento che trattengono questa merce: riprova quando saranno conclusi."),
            CodiceEsito.Conflitto => Localizzatore.Traduci("La giacenza è cambiata nel frattempo: controlla le cifre e riprova."),
            _ => TestoErrore(codice),
        };
        return Page();
    }
}

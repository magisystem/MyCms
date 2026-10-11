using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Areas.Platform.Pages.Tenant;

/// <summary>
/// Credenziali PayPal di un sito (ADR-0020), riservate al <c>PlatformAdmin</c>. Il segreto del client e l'identificativo del webhook
/// sono in sola scrittura: la pagina mostra soltanto se sono impostati e un campo vuoto li lascia invariati.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PagamentiModel : PaginaPiattaforma
{
    private readonly IServizioImpostazioniPagamento _pagamenti;

    public PagamentiModel(IServizioImpostazioniPagamento pagamenti, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagamenti = pagamenti;
    }

    public Guid IdTenant { get; private set; }

    public Guid IdSito { get; private set; }

    public SchedaPagamentoSito? Scheda { get; private set; }

    public AmbientePagamento Ambiente { get; private set; } = AmbientePagamento.Sandbox;

    public string IdClient { get; private set; } = string.Empty;

    public IReadOnlyCollection<string> CampiNonValidi { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, Guid idSito, CancellationToken annullamento)
    {
        IdTenant = id;
        IdSito = idSito;
        Scheda = await _pagamenti.LeggiAsync(IdAttore, id, idSito, annullamento);
        if (Scheda is null)
        {
            return NotFound();
        }

        Ambiente = Scheda.Ambiente;
        IdClient = Scheda.IdClient;
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(Guid id, Guid idSito, string? ambiente, string? idClient, string? segretoClient, string? idWebhook, string? tokenConcorrenza, CancellationToken annullamento)
    {
        if (!Enum.TryParse<AmbientePagamento>(ambiente, ignoreCase: true, out var scelto) || !Enum.IsDefined(scelto))
        {
            MessaggioErrore = TestoErrore(CodiceEsito.DatiNonValidi);
            return RedirectToPage(new { id, idSito });
        }

        var esito = await _pagamenti.SalvaAsync(IdAttore, id, idSito, scelto, idClient, segretoClient, idWebhook, tokenConcorrenza, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Credenziali di pagamento salvate.");
            return RedirectToPage(new { id, idSito });
        }

        if (esito.Codice != CodiceEsito.DatiNonValidi)
        {
            MessaggioErrore = TestoErrore(esito.Codice);
            return RedirectToPage(new { id, idSito });
        }

        // Dati non validi: di nuovo il modulo con i valori inseriti (tranne i segreti) e i campi da correggere.
        IdTenant = id;
        IdSito = idSito;
        Scheda = await _pagamenti.LeggiAsync(IdAttore, id, idSito, annullamento);
        if (Scheda is null)
        {
            return NotFound();
        }

        Ambiente = scelto;
        IdClient = idClient ?? string.Empty;
        CampiNonValidi = esito.CampiNonValidi ?? [];
        Errore = Localizzatore.Traduci("Controlla i campi evidenziati.");
        return Page();
    }

    public string NomeAmbiente(AmbientePagamento ambiente) => ambiente switch
    {
        AmbientePagamento.Sandbox => Localizzatore.Traduci("Sandbox (prove, nessun denaro reale)"),
        AmbientePagamento.Live => Localizzatore.Traduci("Produzione (denaro reale)"),
        _ => ambiente.ToString(),
    };
}

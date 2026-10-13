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
    private readonly IServizioImpostazioniNexi _nexi;
    private readonly IServizioProntezzaNegozio _prontezza;

    public PagamentiModel(IServizioImpostazioniPagamento pagamenti, IServizioImpostazioniNexi nexi, IServizioProntezzaNegozio prontezza, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagamenti = pagamenti;
        _nexi = nexi;
        _prontezza = prontezza;
    }

    /// <summary>Credenziali Nexi XPay del sito (la chiave API non c'è mai: solo se è impostata).</summary>
    public SchedaNexiSito? SchedaNexi { get; private set; }

    public AmbientePagamento AmbienteNexi { get; private set; } = AmbientePagamento.Sandbox;

    /// <summary>Verifica di prontezza del negozio del sito (sola lettura).</summary>
    public RapportoProntezza? Rapporto { get; private set; }

    public string EtichettaControllo(string codice) => codice switch
    {
        "NegozioAttivo" => Localizzatore.Traduci("Negozio attivo per il sito"),
        "CredenzialiPayPal" => Localizzatore.Traduci("Credenziali PayPal complete e leggibili"),
        "WebhookPayPal" => Localizzatore.Traduci("Identificativo del webhook impostato"),
        "CredenzialiNexi" => Localizzatore.Traduci("Credenziali Nexi XPay (chiave API) complete e leggibili"),
        "PagamentiManuali" => Localizzatore.Traduci("Pagamenti manuali attivi (bonifico, contrassegno)"),
        "BonificoIncompleto" => Localizzatore.Traduci("Bonifico attivo ma senza intestatario e IBAN"),
        "InterruttoreProduzione" => Localizzatore.Traduci("Interruttore di produzione acceso (Negozio:CheckoutProduzione)"),
        "AmbienteProva" => Localizzatore.Traduci("Ambiente di prova (nessun denaro reale)"),
        "ProdottiPubblicati" => Localizzatore.Traduci("Almeno un prodotto pubblicato con una variante attiva"),
        "MetodiSpedizione" => Localizzatore.Traduci("Almeno un metodo di spedizione attivo"),
        "CanaleEmail" => Localizzatore.Traduci("Canale email del tenant configurato (conferme e avvisi)"),
        "AmministratoriAttivi" => Localizzatore.Traduci("Almeno un amministratore attivo (avvisi dei nuovi ordini)"),
        "PaginaPrivacy" => Localizzatore.Traduci("Pagina «privacy» pubblicata"),
        "PaginaResi" => Localizzatore.Traduci("Pagina «resi» pubblicata"),
        "ManutenzioneAttiva" => Localizzatore.Traduci("Manutenzione periodica attiva (scadenza prenotazioni, riconciliazione, conservazione)"),
        "DominioPubblico" => Localizzatore.Traduci("Dominio pubblico del sito (indirizzo del webhook)"),
        _ => codice,
    };

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
        SchedaNexi = await _nexi.LeggiAsync(IdAttore, id, idSito, annullamento);
        AmbienteNexi = SchedaNexi?.Ambiente ?? AmbientePagamento.Sandbox;
        Rapporto = await _prontezza.VerificaAsync(IdAttore, id, idSito, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaNexiAsync(Guid id, Guid idSito, string? ambiente, string? chiaveApi, string? tokenConcorrenza, CancellationToken annullamento)
    {
        if (!Enum.TryParse<AmbientePagamento>(ambiente, ignoreCase: true, out var scelto) || !Enum.IsDefined(scelto))
        {
            MessaggioErrore = TestoErrore(CodiceEsito.DatiNonValidi);
            return RedirectToPage(new { id, idSito });
        }

        var esito = await _nexi.SalvaAsync(IdAttore, id, idSito, scelto, chiaveApi, tokenConcorrenza, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Credenziali Nexi salvate.");
        }
        else
        {
            MessaggioErrore = esito.Codice == CodiceEsito.DatiNonValidi
                ? Localizzatore.Traduci("La chiave API Nexi non è valida o manca.")
                : TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id, idSito });
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
        SchedaNexi = await _nexi.LeggiAsync(IdAttore, id, idSito, annullamento);
        AmbienteNexi = SchedaNexi?.Ambiente ?? AmbientePagamento.Sandbox;
        Rapporto = await _prontezza.VerificaAsync(IdAttore, id, idSito, annullamento);
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

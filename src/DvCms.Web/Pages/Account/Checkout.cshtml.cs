using System.Globalization;
using System.Security.Claims;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account;

public sealed class ModuloCheckout
{
    public Guid? IdMetodo { get; set; }

    public string? Destinatario { get; set; }

    public string? IndirizzoRiga1 { get; set; }

    public string? IndirizzoRiga2 { get; set; }

    public string? Cap { get; set; }

    public string? Citta { get; set; }

    public string? Paese { get; set; }

    /// <summary>Come paga il cliente; vuoto = pagamento online (come prima dell'arrivo di bonifico e contrassegno).</summary>
    public ModalitaPagamento? Modalita { get; set; }

    /// <summary>Totale visto dal cliente per ciascuna combinazione consegna e pagamento, con chiave <c>idMetodo:modalità</c> (solo per accorgersi di cambiamenti: il server ricalcola sempre).</summary>
    public Dictionary<string, string> Atteso { get; set; } = [];
}

/// <summary>Un pagamento scelto per un metodo di consegna: se si offre e quanto si pagherebbe (commissione compresa).</summary>
public sealed record OpzionePagamento(ModalitaPagamento Modalita, PreventivoPagamento Preventivo);

public sealed record OpzioneConsegna(MetodoSpedizioneVista Metodo, decimal Totale, IReadOnlyList<OpzionePagamento> Pagamenti);

/// <summary>
/// Cassa (F-003). Il cliente sceglie il metodo di consegna e, se serve, l'indirizzo; non invia mai importi da usare. I totali che
/// vede sono calcolati dal server e il totale visto torna indietro soltanto per accorgersi di variazioni di prezzo o tariffa.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CheckoutModel : PageModel
{
    private readonly IServizioCarrello _carrello;
    private readonly IServizioSpedizioni _spedizioni;
    private readonly IServizioOrdini _ordini;
    private readonly IServizioPagamentiOffline _offline;
    private readonly IServizioPagamenti _pagamenti;
    private readonly IContestoLingua _lingua;
    private readonly ILocalizzatore _localizzatore;

    public CheckoutModel(
        IServizioCarrello carrello,
        IServizioSpedizioni spedizioni,
        IServizioOrdini ordini,
        IServizioPagamentiOffline offline,
        IServizioPagamenti pagamenti,
        IContestoLingua lingua,
        ILocalizzatore localizzatore)
    {
        _carrello = carrello;
        _spedizioni = spedizioni;
        _ordini = ordini;
        _offline = offline;
        _pagamenti = pagamenti;
        _lingua = lingua;
        _localizzatore = localizzatore;
    }

    [BindProperty]
    public ModuloCheckout Modulo { get; set; } = new();

    public string? Errore { get; private set; }

    public IReadOnlyList<string> CampiNonValidi { get; private set; } = [];

    /// <summary>Codici degli articoli di cui non c'è abbastanza giacenza.</summary>
    public IReadOnlyList<string> Mancanti { get; private set; } = [];

    public CarrelloVista Vista { get; private set; } = new([], [], 0m, 0m, 0m, "EUR");

    public IReadOnlyList<OpzioneConsegna> Opzioni { get; private set; } = [];

    /// <summary>I pagamenti che il sito offre, nell'ordine di presentazione (online, bonifico, contrassegno).</summary>
    public IReadOnlyList<ModalitaPagamento> ModalitaOfferte { get; private set; } = [ModalitaPagamento.Online];

    public ImpostazioniPagamentoOffline Offline { get; private set; } = ImpostazioniPagamentoOffline.Spente;

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    private Guid IdAccount => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        await CaricaAsync(annullamento);
        return Vista.Righe.Count == 0 && Vista.NonDisponibili.Count == 0 ? RedirectToPage("/Account/Carrello") : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        if (Modulo.IdMetodo is not { } metodo)
        {
            await CaricaAsync(annullamento);
            Errore = _localizzatore.Traduci("Scegli un metodo di consegna.");
            CampiNonValidi = ["IdMetodo"];
            return Page();
        }

        // Il totale visto è un controllo di coerenza; se manca o è illeggibile si usa uno impossibile e l'ordine non si crea.
        var modalita = Modulo.Modalita ?? ModalitaPagamento.Online;
        // Una cassa aperta prima dell'arrivo dei pagamenti manuali invia il totale con la sola chiave del metodo: vale per il pagamento online.
        var trovato = Modulo.Atteso.TryGetValue(string.Create(CultureInfo.InvariantCulture, $"{metodo:D}:{modalita}"), out var testo)
            || (modalita == ModalitaPagamento.Online && Modulo.Atteso.TryGetValue(metodo.ToString("D", CultureInfo.InvariantCulture), out testo));
        var atteso = trovato && decimal.TryParse(testo, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var letto) ? letto : -1m;
        var dati = new DatiOrdine(metodo, Modulo.Destinatario, Modulo.IndirizzoRiga1, Modulo.IndirizzoRiga2, Modulo.Cap, Modulo.Citta, Modulo.Paese, atteso, modalita);
        var esito = await _ordini.CreaDaCarrelloAsync(IdAccount, _lingua.CodiceLingua, dati, annullamento);
        if (esito.Riuscito)
        {
            return Redirect($"/account/ordini/{esito.Id}");
        }

        if (esito.Codice == CodiceEsito.DatiNonValidi && esito.CampiNonValidi is ["Carrello"])
        {
            return RedirectToPage("/Account/Carrello");
        }

        await CaricaAsync(annullamento);
        CampiNonValidi = esito.CampiNonValidi ?? [];
        Mancanti = [.. CampiNonValidi.Where(c => c.StartsWith("Giacenza:", StringComparison.Ordinal)).Select(c => c["Giacenza:".Length..])];
        Errore = esito.Codice switch
        {
            CodiceEsito.Conflitto when Mancanti.Count > 0 => _localizzatore.Traduci("Alcuni articoli non sono disponibili nella quantità richiesta: riduci la quantità o toglili dal carrello."),
            CodiceEsito.Conflitto => _localizzatore.Traduci("Il carrello, i prezzi o le tariffe sono cambiati: controlla il riepilogo e conferma di nuovo."),
            CodiceEsito.NonTrovato => _localizzatore.Traduci("Il metodo di consegna scelto non è più disponibile."),
            CodiceEsito.DatiNonValidi when CampiNonValidi.Contains("ModalitaPagamento") => _localizzatore.Traduci("Il metodo di pagamento scelto non è disponibile per questo ordine."),
            CodiceEsito.DatiNonValidi => _localizzatore.Traduci("Controlla i dati inseriti."),
            CodiceEsito.NonConsentito => _localizzatore.Traduci("Il negozio non è disponibile."),
            _ => _localizzatore.Traduci("Operazione non riuscita."),
        };
        return Page();
    }

    public string NomeCampo(string campo) => campo switch
    {
        _ when campo.StartsWith("Giacenza:", StringComparison.Ordinal) => campo["Giacenza:".Length..],
        "IdMetodo" => _localizzatore.Traduci("Metodo di consegna"),
        "ModalitaPagamento" => _localizzatore.Traduci("Metodo di pagamento"),
        "Destinatario" => _localizzatore.Traduci("Destinatario"),
        "IndirizzoRiga1" => _localizzatore.Traduci("Indirizzo"),
        "IndirizzoRiga2" => _localizzatore.Traduci("Indirizzo (seconda riga)"),
        "Cap" => _localizzatore.Traduci("CAP"),
        "Citta" => _localizzatore.Traduci("Città"),
        "Paese" => _localizzatore.Traduci("Paese (codice a due lettere valido per il metodo scelto)"),
        "Carrello" => _localizzatore.Traduci("Carrello"),
        "Totale" => _localizzatore.Traduci("Totale"),
        _ => campo,
    };

    private async Task CaricaAsync(CancellationToken annullamento)
    {
        Vista = await _carrello.LeggiAsync(IdAccount, _lingua.CodiceLingua, annullamento);
        var metodi = await _spedizioni.ElencaAttiviAsync(IdAccount, annullamento);
        Offline = await _offline.LeggiPerClienteAsync(IdAccount, annullamento);

        // Il pagamento online si offre se c'è un provider pronto, o se non c'è alcun pagamento manuale (la pagina dell'ordine spiega cosa manca).
        var providerOnline = await _pagamenti.ProviderDisponibiliAsync(annullamento);
        var offerte = new List<ModalitaPagamento>();
        if (providerOnline.Count > 0 || !(Offline.BonificoAttivo || Offline.ContrassegnoAttivo))
        {
            offerte.Add(ModalitaPagamento.Online);
        }

        if (Offline.BonificoAttivo)
        {
            offerte.Add(ModalitaPagamento.Bonifico);
        }

        if (Offline.ContrassegnoAttivo)
        {
            offerte.Add(ModalitaPagamento.Contrassegno);
        }

        ModalitaOfferte = offerte;
        Opzioni = [.. metodi.Select(m =>
        {
            var base_ = Vista.TotaleLordo + m.TariffaLorda;
            return new OpzioneConsegna(
                m,
                base_,
                [.. offerte.Select(modalita => new OpzionePagamento(modalita, PreventivoPagamento.Calcola(Offline, modalita, m.Tipo, m.Area, base_)))]);
        })];
    }
}

using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Pagamenti;

/// <summary>Modulo dei pagamenti manuali: gli importi arrivano come testo (virgola o punto) e li legge il server.</summary>
public sealed class ModuloPagamentiOffline
{
    public bool BonificoAttivo { get; set; }

    public string? Intestatario { get; set; }

    public string? Iban { get; set; }

    public string? Bic { get; set; }

    public string? NomeBanca { get; set; }

    public string? GiorniPerPagare { get; set; }

    public string? IstruzioniBonifico { get; set; }

    public bool ContrassegnoAttivo { get; set; }

    public string? CommissioneFissa { get; set; }

    public string? CommissionePercentuale { get; set; }

    public string? CommissioneGratisSopra { get; set; }

    public string? OrdineMinimo { get; set; }

    public string? OrdineMassimo { get; set; }

    public string? IstruzioniContrassegno { get; set; }

    public string? TokenConcorrenza { get; set; }
}

/// <summary>
/// Pagamenti manuali del sito (ADR-0023): bonifico bancario e contrassegno, attivabili dal <c>TenantAdmin</c>. 404 se il sito non ha il
/// negozio attivo. Il server rivalida tutto; gli importi di un ordine non si leggono mai da qui al momento dell'acquisto.
/// </summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioPagamentiOffline _servizio;
    private readonly IServizioCatalogo _catalogo;

    public IndexModel(IServizioPagamentiOffline servizio, IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _servizio = servizio;
        _catalogo = catalogo;
    }

    public ModuloPagamentiOffline Modulo { get; private set; } = new();

    public IReadOnlyList<string> CampiNonValidi { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Modulo = Compila(await _servizio.LeggiAsync(IdAttore, annullamento));
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(ModuloPagamentiOffline modulo, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(modulo);
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var errori = new List<string>();
        var giorni = LeggiIntero(modulo.GiorniPerPagare, ImpostazioniPagamentoOfflineSito.GiorniPerPagarePredefiniti, errori, "GiorniPerPagare");
        var fissa = LeggiImporto(modulo.CommissioneFissa, 0m, errori, "CommissioneFissa") ?? 0m;
        var percentuale = LeggiImporto(modulo.CommissionePercentuale, 0m, errori, "CommissionePercentuale") ?? 0m;
        var gratis = LeggiImporto(modulo.CommissioneGratisSopra, null, errori, "CommissioneGratisSopra");
        var minimo = LeggiImporto(modulo.OrdineMinimo, null, errori, "OrdineMinimo");
        var massimo = LeggiImporto(modulo.OrdineMassimo, null, errori, "OrdineMassimo");
        if (errori.Count == 0)
        {
            var dati = new ImpostazioniPagamentoOffline(
                modulo.BonificoAttivo,
                modulo.Intestatario,
                modulo.Iban,
                modulo.Bic,
                modulo.NomeBanca,
                giorni,
                modulo.IstruzioniBonifico,
                modulo.ContrassegnoAttivo,
                fissa,
                percentuale,
                gratis,
                minimo,
                massimo,
                modulo.IstruzioniContrassegno);
            var esito = await _servizio.SalvaAsync(IdAttore, dati, modulo.TokenConcorrenza, annullamento);
            if (esito.Riuscito)
            {
                Messaggio = Localizzatore.Traduci("Pagamenti salvati.");
                return RedirectToPage();
            }

            errori.AddRange(esito.CampiNonValidi ?? []);
            Errore = esito.Codice switch
            {
                CodiceEsito.Conflitto => Localizzatore.Traduci("Le impostazioni sono state modificate da un altro amministratore: ricarica la pagina e riprova."),
                _ => TestoErrore(esito.Codice),
            };
        }
        else
        {
            Errore = TestoErrore(CodiceEsito.DatiNonValidi);
        }

        // Si rimostra il modulo com'è stato inviato, con il token corrente se l'invio non ha avuto effetto.
        Modulo = modulo;
        CampiNonValidi = errori;
        return Page();
    }

    public string NomeCampo(string campo) => campo switch
    {
        "Intestatario" => Localizzatore.Traduci("Intestatario del conto"),
        "Iban" => Localizzatore.Traduci("IBAN"),
        "Bic" => Localizzatore.Traduci("BIC/SWIFT"),
        "NomeBanca" => Localizzatore.Traduci("Nome della banca"),
        "GiorniPerPagare" => Localizzatore.Traduci("Giorni per pagare"),
        "IstruzioniBonifico" => Localizzatore.Traduci("Istruzioni per il bonifico"),
        "CommissioneFissa" => Localizzatore.Traduci("Commissione fissa"),
        "CommissionePercentuale" => Localizzatore.Traduci("Commissione percentuale"),
        "CommissioneGratisSopra" => Localizzatore.Traduci("Commissione gratuita da questo totale"),
        "OrdineMinimo" => Localizzatore.Traduci("Totale minimo dell'ordine"),
        "OrdineMassimo" => Localizzatore.Traduci("Totale massimo dell'ordine"),
        "IstruzioniContrassegno" => Localizzatore.Traduci("Istruzioni per il contrassegno"),
        _ => campo,
    };

    public bool NonValido(string campo) => CampiNonValidi.Contains(campo);

    private static ModuloPagamentiOffline Compila(SchedaPagamentoOffline scheda)
    {
        var i = scheda.Impostazioni;
        string Importo(decimal valore) => valore.ToString("0.00", CultureInfo.InvariantCulture);
        string? ImportoOpzionale(decimal? valore) => valore is { } v ? Importo(v) : null;
        return new ModuloPagamentiOffline
        {
            BonificoAttivo = i.BonificoAttivo,
            Intestatario = i.Intestatario,
            Iban = i.Iban is null ? null : Iban.Formatta(i.Iban),
            Bic = i.Bic,
            NomeBanca = i.NomeBanca,
            GiorniPerPagare = i.GiorniPerPagare.ToString(CultureInfo.InvariantCulture),
            IstruzioniBonifico = i.IstruzioniBonifico,
            ContrassegnoAttivo = i.ContrassegnoAttivo,
            CommissioneFissa = Importo(i.CommissioneFissa),
            CommissionePercentuale = Importo(i.CommissionePercentuale),
            CommissioneGratisSopra = ImportoOpzionale(i.CommissioneGratisSopra),
            OrdineMinimo = ImportoOpzionale(i.OrdineMinimo),
            OrdineMassimo = ImportoOpzionale(i.OrdineMassimo),
            IstruzioniContrassegno = i.IstruzioniContrassegno,
            TokenConcorrenza = scheda.TokenConcorrenza,
        };
    }

    /// <summary>Vuoto = il valore predefinito (nullo per i limiti facoltativi); virgola o punto come separatore; qualsiasi altra cosa è un campo non valido.</summary>
    private static decimal? LeggiImporto(string? testo, decimal? predefinito, List<string> errori, string campo)
    {
        if (string.IsNullOrWhiteSpace(testo))
        {
            return predefinito;
        }

        if (decimal.TryParse(testo.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valore))
        {
            return valore;
        }

        errori.Add(campo);
        return predefinito;
    }

    private static int LeggiIntero(string? testo, int predefinito, List<string> errori, string campo)
    {
        if (string.IsNullOrWhiteSpace(testo))
        {
            return predefinito;
        }

        if (int.TryParse(testo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var valore))
        {
            return valore;
        }

        errori.Add(campo);
        return predefinito;
    }
}

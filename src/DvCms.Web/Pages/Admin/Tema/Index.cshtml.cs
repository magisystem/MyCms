using DvCms.Application.Localizzazione;
using DvCms.Application.Temi;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Tema;

/// <summary>Scelta del tema del sito e delle sue impostazioni (D-001): solo temi del registro, valori validati dallo schema.</summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioTemi _temi;

    public IndexModel(IServizioTemi temi, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _temi = temi;
    }

    public ConfigurazioneTema? Configurazione { get; private set; }

    /// <summary>Il tema di cui si mostrano i campi: quello scelto nel modulo (parametro <c>tema</c>) oppure quello attuale.</summary>
    [BindProperty(SupportsGet = true, Name = "tema")]
    public string? ChiaveScelta { get; set; }

    public DefinizioneTema? TemaMostrato { get; private set; }

    public IReadOnlyDictionary<string, string> Valori { get; private set; } = new Dictionary<string, string>();

    public IReadOnlyCollection<string> CampiNonValidi { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        await CaricaAsync(annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? tokenConcorrenza, CancellationToken annullamento)
    {
        var valori = Request.Form
            .Where(campo => campo.Key.StartsWith("t_", StringComparison.Ordinal))
            .ToDictionary(campo => campo.Key[2..], campo => campo.Value.ToString(), StringComparer.Ordinal);
        var esito = await _temi.SalvaAsync(IdAttore, ChiaveScelta, valori, tokenConcorrenza, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Tema salvato.");
            return RedirectToPage();
        }

        Errore = TestoErrore(esito.Codice);
        CampiNonValidi = esito.CampiNonValidi ?? [];
        await CaricaAsync(annullamento);
        Valori = valori;
        return Page();
    }

    private async Task CaricaAsync(CancellationToken annullamento)
    {
        Configurazione = await _temi.LeggiAsync(IdAttore, annullamento);
        var scelto = Configurazione.Disponibili.FirstOrDefault(tema => tema.Chiave == ChiaveScelta)
            ?? Configurazione.Disponibili.First(tema => tema.Chiave == Configurazione.Chiave);
        ChiaveScelta = scelto.Chiave;
        TemaMostrato = scelto.Chiave == Configurazione.Chiave
            ? Configurazione.Disponibili.FirstOrDefault(tema => tema.Chiave == scelto.Chiave && tema.Versione == Configurazione.Versione) ?? scelto
            : scelto;

        // Cambiando tema i valori salvati (del tema precedente) non si riportano.
        Valori = scelto.Chiave == Configurazione.Chiave ? Configurazione.Valori : new Dictionary<string, string>();
    }
}

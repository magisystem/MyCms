using DvCms.Contracts.Email;
using DvCms.Contracts.Localizzazione;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Email.Modelli;

/// <summary>
/// Modifica di un modello email in una lingua, con anteprima e ripristino di quello di fabbrica (ADR-0025). L'HTML dell'anteprima si
/// mostra solo in un riquadro isolato (<c>sandbox</c> senza permessi): quel che scrive il tenant non può agire nell'admin.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ModificaModel : PaginaAdmin
{
    private readonly IServizioModelliEmail _modelli;

    public ModificaModel(IServizioModelliEmail modelli, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _modelli = modelli;
    }

    public SchedaModelloEmail Scheda { get; private set; } = null!;

    [BindProperty]
    public string? Oggetto { get; set; }

    [BindProperty]
    public string? CorpoHtml { get; set; }

    [BindProperty]
    public string? CorpoTesto { get; set; }

    [BindProperty]
    public string? TokenConcorrenza { get; set; }

    /// <summary>Errori di validazione del contenuto proposto.</summary>
    public IReadOnlyList<string> ErroriContenuto { get; private set; } = [];

    public AnteprimaModelloEmail? Anteprima { get; private set; }

    public async Task<IActionResult> OnGetAsync(string codice, string? lingua, CancellationToken annullamento)
    {
        var esito = await CaricaAsync(codice, lingua, annullamento);
        if (esito is not null)
        {
            return esito;
        }

        Oggetto = Scheda.Contenuto.Oggetto;
        CorpoHtml = Scheda.Contenuto.CorpoHtml;
        CorpoTesto = Scheda.Contenuto.CorpoTesto;
        TokenConcorrenza = Scheda.TokenConcorrenza;
        return Page();
    }

    public async Task<IActionResult> OnPostAnteprimaAsync(string codice, string? lingua, CancellationToken annullamento)
    {
        var esito = await CaricaAsync(codice, lingua, annullamento);
        if (esito is not null)
        {
            return esito;
        }

        Anteprima = await _modelli.AnteprimaAsync(IdAttore, codice, Proposto(), annullamento);
        ErroriContenuto = Anteprima?.Validazione.Errori ?? [];
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(string codice, string? lingua, CancellationToken annullamento)
    {
        var esito = await _modelli.SalvaAsync(IdAttore, codice, lingua ?? string.Empty, Proposto(), TokenConcorrenza, annullamento);
        switch (esito.Codice)
        {
            case CodiceEsito.Riuscito:
                Messaggio = Localizzatore.Traduci("Modello salvato.");
                return RedirectToPage(new { codice, lingua });
            case CodiceEsito.DatiNonValidi when esito.Validazione.Errori.Count > 0:
                var caricata = await CaricaAsync(codice, lingua, annullamento);
                if (caricata is not null)
                {
                    return caricata;
                }

                ErroriContenuto = esito.Validazione.Errori;
                Errore = Localizzatore.Traduci("Il modello non è valido: correggi gli errori sotto.");
                return Page();
            default:
                MessaggioErrore = TestoErrore(esito.Codice);
                return RedirectToPage(new { codice, lingua });
        }
    }

    public async Task<IActionResult> OnPostRipristinaAsync(string codice, string? lingua, CancellationToken annullamento)
    {
        var esito = await _modelli.RipristinaAsync(IdAttore, codice, lingua ?? string.Empty, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Modello di fabbrica ripristinato.");
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { codice, lingua });
    }

    private ContenutoModelloEmail Proposto() => new(Oggetto ?? string.Empty, CorpoHtml ?? string.Empty, CorpoTesto ?? string.Empty);

    /// <summary>Carica la scheda; restituisce la risposta da dare se non si può mostrare, oppure <c>null</c> se è caricata.</summary>
    private async Task<IActionResult?> CaricaAsync(string codice, string? lingua, CancellationToken annullamento)
    {
        try
        {
            var scheda = await _modelli.LeggiAsync(IdAttore, codice, lingua ?? string.Empty, annullamento);
            if (scheda is null)
            {
                return NotFound();
            }

            Scheda = scheda;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}

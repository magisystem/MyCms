using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Moduli;
using DvCms.Core.Moduli;
using DvCms.DTO.Moduli;
using DvCms.Web.Accessi;
using DvCms.Web.AntiAbuso;
using DvCms.Web.Localizzazione;
using DvCms.Web.Pubblico;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DvCms.Web.Pages.Pubblico;

/// <summary>
/// Invio di un modulo di contatto dal sito pubblico (ADR-0027). L'ordine è fisso: protezione accesa (altrimenti il modulo non accetta nulla
/// fuori dallo sviluppo), modulo pubblicato nel sito dell'host, prova ALTCHA, limite di richieste, validazione contro la definizione
/// corrente, salvataggio. Dopo un invio riuscito si reindirizza (post/redirect/get): alla pagina scelta dal tenant o alla conferma.
/// </summary>
[EnableRateLimiting(Politiche.LimiteAccesso)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ModuloModel : PageModel
{
    private readonly IServizioModuloPubblico _moduli;
    private readonly IVerificaAntiAbuso _antiAbuso;
    private readonly IHostEnvironment _ambiente;
    private readonly ILocalizzatore _localizzatore;
    private readonly IContestoLingua _lingua;

    public ModuloModel(IServizioModuloPubblico moduli, IVerificaAntiAbuso antiAbuso, IHostEnvironment ambiente, ILocalizzatore localizzatore, IContestoLingua lingua)
    {
        _moduli = moduli;
        _antiAbuso = antiAbuso;
        _ambiente = ambiente;
        _localizzatore = localizzatore;
        _lingua = lingua;
    }

    public ModelloFormModulo? Form { get; private set; }

    /// <summary>Messaggio quando non c'è una form da mostrare (modulo non disponibile, limite raggiunto).</summary>
    public string? Avviso { get; private set; }

    /// <summary>Un modulo non si apre con un indirizzo digitato: la form sta nelle pagine del sito.</summary>
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken annullamento)
    {
        // Un modulo pubblico senza protezione non deve funzionare per una dimenticanza di configurazione (ADR-0027, D6).
        if (!_antiAbuso.Attiva && !_ambiente.IsDevelopment())
        {
            return Rifiuta(StatusCodes.Status503ServiceUnavailable, _localizzatore.Traduci("Il modulo non è al momento disponibile."));
        }

        var modulo = await _moduli.LeggiAsync(id, Request.Form["lingua"].FirstOrDefault(), annullamento);
        if (modulo is null)
        {
            return NotFound();
        }

        HttpContext.Items[ContestoLinguaRichiesta.ChiaveElemento] = modulo.Lingua;
        var valori = Request.Form
            .Where(campo => campo.Key.StartsWith(ElaboratoreInvioModulo.PrefissoCampo, StringComparison.Ordinal))
            .ToDictionary(campo => campo.Key, campo => campo.Value.Select(v => v ?? string.Empty).ToArray(), StringComparer.Ordinal);

        if (_antiAbuso.Attiva && !await _antiAbuso.VerificaAsync(Request, annullamento))
        {
            return Mostra(modulo, valori.ToDictionary(v => v.Key[ElaboratoreInvioModulo.PrefissoCampo.Length..], v => v.Value, StringComparer.Ordinal), null, _localizzatore.Traduci("Non siamo riusciti a verificare che tu non sia un programma automatico. Riprova."));
        }

        var risultato = await _moduli.InviaAsync(id, modulo.Lingua, valori, annullamento);
        switch (risultato.Esito)
        {
            case EsitoInvioPubblico.Accettato:
                return string.IsNullOrWhiteSpace(modulo.Definizione.PercorsoDopoInvio)
                    ? Redirect($"/moduli/{id:D}/inviato?lingua={Uri.EscapeDataString(modulo.Lingua)}")
                    : LocalRedirect(modulo.Definizione.PercorsoDopoInvio);
            case EsitoInvioPubblico.DatiNonValidi:
                return Mostra(
                    modulo,
                    risultato.ValoriRicevuti,
                    risultato.Errori.ToDictionary(e => e.Key, e => TestoErrore(e.Value), StringComparer.Ordinal),
                    _localizzatore.Traduci("Controlla i campi evidenziati."));
            case EsitoInvioPubblico.LimiteRaggiunto:
                return Rifiuta(StatusCodes.Status429TooManyRequests, _localizzatore.Traduci("Questo modulo ha ricevuto troppe richieste: riprova più tardi."));
            default:
                return NotFound();
        }
    }

    private PageResult Mostra(ModuloPubblico modulo, IReadOnlyDictionary<string, string[]> valori, IReadOnlyDictionary<string, string>? errori, string errore)
    {
        Form = new ModelloFormModulo(
            modulo.Id,
            modulo.Definizione,
            modulo.Lingua,
            modulo.LinguaPredefinita,
            Anteprima: false,
            Azione: $"/moduli/{modulo.Id:D}",
            ConProvaAntiAbuso: _antiAbuso.Attiva,
            valori,
            errori,
            errore);
        return Page();
    }

    private PageResult Rifiuta(int stato, string avviso)
    {
        Avviso = avviso;
        Response.StatusCode = stato;
        return Page();
    }

    private string TestoErrore(ErroreCampoModulo errore) => errore switch
    {
        ErroreCampoModulo.Obbligatorio => _localizzatore.Traduci("Questo campo è obbligatorio."),
        ErroreCampoModulo.TroppoLungo => _localizzatore.Traduci("Il testo è troppo lungo."),
        _ => _localizzatore.Traduci("Il valore non è valido."),
    };
}

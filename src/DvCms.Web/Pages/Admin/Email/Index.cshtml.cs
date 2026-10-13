using DvCms.Application.Email;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using DvCms.Models.Email;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Email;

/// <summary>
/// Email del tenant con Microsoft 365 o Google Mail (ADR-0021): app OAuth, collegamento della casella e invio di prova. Solo il
/// <c>TenantAdmin</c> del tenant (politica della cartella e riverifica nel servizio): il <c>PlatformAdmin</c> non vi accede.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PaginaAdmin
{
    private const string PrefissoValore = "v_";
    private const string ChiaveUrlConsenso = "UrlConsenso";

    private static readonly string[] _prefissiConsentiti = ["https://login.microsoftonline.com/", "https://accounts.google.com/"];

    /// <summary>Da quanti giorni prima della scadenza del segreto del client la pagina avvisa.</summary>
    public const int GiorniPreavvisoScadenza = 30;

    private readonly IServizioConnessioneEmail _email;
    private readonly TimeProvider _orologio;

    public IndexModel(IServizioConnessioneEmail email, TimeProvider orologio, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _email = email;
        _orologio = orologio;
    }

    /// <summary>Giorni alla scadenza del segreto del client (negativi se già scaduto), se il tenant ne ha indicato la data.</summary>
    public int? GiorniAllaScadenzaSegreto =>
        Scheda.App is { } app && app.Valori.TryGetValue(DefinizioniProviderEmail.CampoScadenzaSegreto, out var testo) && DefinizioniProviderEmail.TentaLeggereData(testo, out var data)
            ? data.DayNumber - DateOnly.FromDateTime(_orologio.GetUtcNow().UtcDateTime).DayNumber
            : null;

    public SchedaConnessioneEmail Scheda { get; private set; } = new(null, null, null, []);

    /// <summary>Canale mostrato nel modulo (quello salvato oppure quello scelto).</summary>
    public ProviderEmail Provider { get; private set; } = ProviderEmail.MicrosoftOAuth;

    public IReadOnlyList<CampoProviderEmail> Campi => DefinizioniProviderEmail.Campi(Provider);

    public IReadOnlyDictionary<string, string> Valori { get; private set; } = new Dictionary<string, string>();

    public bool SegretoImpostato { get; private set; }

    public IReadOnlyCollection<string> CampiNonValidi { get; private set; } = [];

    public string? TokenConcorrenza { get; private set; }

    /// <summary>L'indirizzo di ritorno da registrare nell'app presso Microsoft o Google.</summary>
    public string UrlRitorno => Url.PageLink("/Admin/Email/Collegamento") ?? string.Empty;

    public static IReadOnlyList<ProviderEmail> Canali { get; } = [ProviderEmail.MicrosoftOAuth, ProviderEmail.GoogleOAuth];

    public async Task<IActionResult> OnGetAsync(string? provider, CancellationToken annullamento)
    {
        if (!await CaricaAsync(annullamento))
        {
            return Forbid();
        }

        if (Enum.TryParse<ProviderEmail>(provider, ignoreCase: true, out var scelto) && scelto.UsaOAuth())
        {
            Provider = scelto;
            if (Scheda.App?.Provider != scelto)
            {
                Valori = new Dictionary<string, string>();
                SegretoImpostato = false;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAppAsync(string? provider, string? tokenConcorrenza, bool sostituisciCanale, CancellationToken annullamento)
    {
        if (!Enum.TryParse<ProviderEmail>(provider, ignoreCase: true, out var scelto) || !scelto.UsaOAuth())
        {
            MessaggioErrore = TestoErrore(CodiceEsito.DatiNonValidi);
            return RedirectToPage();
        }

        var valori = Request.Form
            .Where(campo => campo.Key.StartsWith(PrefissoValore, StringComparison.Ordinal))
            .ToDictionary(campo => campo.Key[PrefissoValore.Length..], campo => campo.Value.ToString(), StringComparer.Ordinal);
        var esito = await _email.SalvaAppAsync(IdAttore, scelto, valori, tokenConcorrenza, sostituisciCanale, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("App salvata. Ora collega la casella.");
            return RedirectToPage();
        }

        if (esito.Codice != CodiceEsito.DatiNonValidi)
        {
            MessaggioErrore = esito.Codice switch
            {
                CodiceEsito.ChiaveSegretiMancante => Localizzatore.Traduci("La chiave per cifrare i segreti non è configurata sul server."),
                CodiceEsito.NonConsentito => Localizzatore.Traduci("Il tenant usa già un canale configurato dalla piattaforma: per sostituirlo spunta la conferma."),
                _ => TestoErrore(esito.Codice),
            };
            return RedirectToPage();
        }

        // Dati non validi: si mostra di nuovo il modulo con i valori inseriti (tranne il segreto) e i campi da correggere.
        if (!await CaricaAsync(annullamento))
        {
            return Forbid();
        }

        Provider = scelto;
        Valori = valori.Where(c => Campi.Any(campo => campo.Nome == c.Key && !campo.Segreto)).ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        SegretoImpostato = Scheda.App?.Provider == scelto && Scheda.App.SegretoImpostato;
        CampiNonValidi = esito.CampiNonValidi;
        Errore = Localizzatore.Traduci("Controlla i campi evidenziati.");
        return Page();
    }

    public async Task<IActionResult> OnPostCollegaAsync(CancellationToken annullamento)
    {
        var esito = await _email.AvviaCollegamentoAsync(IdAttore, UrlRitorno, annullamento);
        if (!esito.Riuscito || esito.UrlAutorizzazione is null || esito.StatoProtetto is null)
        {
            MessaggioErrore = esito.Codice switch
            {
                CodiceEsito.NonConsentito => Localizzatore.Traduci("Salva prima l'app OAuth del canale."),
                CodiceEsito.ChiaveSegretiMancante => Localizzatore.Traduci("La chiave per cifrare i segreti non è configurata sul server."),
                _ => TestoErrore(esito.Codice),
            };
            return RedirectToPage();
        }

        CookieCollegamentoEmail.Imposta(HttpContext, esito.StatoProtetto);

        // Il reindirizzamento al provider passa da una navigazione (GET): la direttiva form-action 'self' blocca in alcuni browser
        // un reindirizzamento verso un altro sito subito dopo l'invio di un modulo.
        TempData[ChiaveUrlConsenso] = esito.UrlAutorizzazione;
        return RedirectToPage(pageName: null, pageHandler: "Vai");
    }

    public IActionResult OnGetVai()
    {
        if (TempData[ChiaveUrlConsenso] is string url && _prefissiConsentiti.Any(prefisso => url.StartsWith(prefisso, StringComparison.Ordinal)))
        {
            return Redirect(url);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostScollegaAsync(CancellationToken annullamento)
    {
        var esito = await _email.ScollegaAsync(IdAttore, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Casella scollegata.");
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRimuoviAsync(CancellationToken annullamento)
    {
        var esito = await _email.RimuoviCanaleAsync(IdAttore, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Canale rimosso: ora la piattaforma può configurarne un altro.");
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostProvaAsync(string? destinatario, CancellationToken annullamento)
    {
        var messaggio = new MessaggioEmail(
            destinatario ?? string.Empty,
            null,
            Localizzatore.Traduci("Email di prova"),
            Localizzatore.Traduci("Questa è un'email di prova inviata dalla configurazione del tenant. Se la ricevi, l'invio funziona."));
        var esito = await _email.AccodaProvaAsync(IdAttore, destinatario, messaggio, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Email di prova accodata: l'invio avviene in background, l'eventuale errore comparirà qui sotto.");
        }
        else
        {
            MessaggioErrore = esito.Codice == CodiceEsito.NonConsentito
                ? Localizzatore.Traduci("Collega prima la casella.")
                : TestoErrore(esito.Codice);
        }

        return RedirectToPage();
    }

    public string NomeProvider(ProviderEmail provider) => provider switch
    {
        ProviderEmail.MicrosoftOAuth => Localizzatore.Traduci("Microsoft 365"),
        ProviderEmail.GoogleOAuth => Localizzatore.Traduci("Google Mail"),
        ProviderEmail.Smtp => Localizzatore.Traduci("SMTP"),
        ProviderEmail.Graph => Localizzatore.Traduci("Microsoft Graph"),
        ProviderEmail.Gmail => Localizzatore.Traduci("Gmail"),
        ProviderEmail.SendGrid => Localizzatore.Traduci("SendGrid"),
        _ => provider.ToString(),
    };

    public string Etichetta(string campo) => campo switch
    {
        "TenantMicrosoft" => Localizzatore.Traduci("Tenant Microsoft"),
        "IdClient" => Localizzatore.Traduci("Identificativo client"),
        "SegretoClient" => Localizzatore.Traduci("Segreto del client"),
        DefinizioniProviderEmail.CampoScadenzaSegreto => Localizzatore.Traduci("Scadenza del segreto del client (facoltativa)"),
        _ => campo,
    };

    private async Task<bool> CaricaAsync(CancellationToken annullamento)
    {
        try
        {
            Scheda = await _email.LeggiAsync(IdAttore, annullamento);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        Provider = Scheda.App?.Provider ?? ProviderEmail.MicrosoftOAuth;
        Valori = Scheda.App?.Valori ?? new Dictionary<string, string>();
        SegretoImpostato = Scheda.App?.SegretoImpostato ?? false;
        TokenConcorrenza = Scheda.App?.TokenConcorrenza;
        return true;
    }
}

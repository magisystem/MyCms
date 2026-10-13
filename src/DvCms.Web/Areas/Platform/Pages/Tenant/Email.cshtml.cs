using DvCms.Application.Email;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using DvCms.Models.Email;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Areas.Platform.Pages.Tenant;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EmailModel : PaginaPiattaforma
{
    private const string PrefissoValore = "v_";
    private const string PrefissoRimozione = "r_";

    private readonly IServizioImpostazioniEmail _email;

    public EmailModel(IServizioImpostazioniEmail email, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _email = email;
    }

    public Guid IdTenant { get; private set; }

    public SchedaEmailTenant? Scheda { get; private set; }

    public ProviderEmail Provider { get; private set; } = ProviderEmail.Smtp;

    /// <summary>Il canale con consenso OAuth è del <c>TenantAdmin</c>: qui si vede soltanto che c'è.</summary>
    public bool CanaleGestitoDalTenant => Scheda?.Impostazioni?.Provider.UsaOAuth() == true;

    /// <summary>I canali che il <c>PlatformAdmin</c> può configurare (quelli con consenso OAuth li gestisce il tenant).</summary>
    public static IReadOnlyList<ProviderEmail> CanaliConfigurabili { get; } = [ProviderEmail.Smtp, ProviderEmail.Graph, ProviderEmail.Gmail, ProviderEmail.SendGrid];

    public IReadOnlyList<CampoProviderEmail> Campi => DefinizioniProviderEmail.Campi(Provider);

    /// <summary>Valori da mostrare nei campi (mai i segreti).</summary>
    public IReadOnlyDictionary<string, string> Valori { get; private set; } = new Dictionary<string, string>();

    public IReadOnlySet<string> SegretiImpostati { get; private set; } = new HashSet<string>();

    public IReadOnlyCollection<string> CampiNonValidi { get; private set; } = [];

    public string? TokenConcorrenza { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? provider, CancellationToken annullamento)
    {
        IdTenant = id;
        Scheda = await _email.LeggiAsync(IdAttore, id, annullamento);
        if (Scheda is null)
        {
            return NotFound();
        }

        Provider = Enum.TryParse<ProviderEmail>(provider, ignoreCase: true, out var scelto) && Enum.IsDefined(scelto) && !scelto.UsaOAuth()
            ? scelto
            : Scheda.Impostazioni?.Provider is { } salvato && !salvato.UsaOAuth() ? salvato : ProviderEmail.Smtp;
        PreparaValori();
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(Guid id, string? provider, string? tokenConcorrenza, CancellationToken annullamento)
    {
        if (!Enum.TryParse<ProviderEmail>(provider, ignoreCase: true, out var scelto) || !Enum.IsDefined(scelto))
        {
            MessaggioErrore = TestoErrore(CodiceEsito.DatiNonValidi);
            return RedirectToPage(new { id });
        }

        var valori = Request.Form
            .Where(campo => campo.Key.StartsWith(PrefissoValore, StringComparison.Ordinal))
            .ToDictionary(campo => campo.Key[PrefissoValore.Length..], campo => campo.Value.ToString(), StringComparer.Ordinal);
        var rimozioni = Request.Form.Keys
            .Where(chiave => chiave.StartsWith(PrefissoRimozione, StringComparison.Ordinal))
            .Select(chiave => chiave[PrefissoRimozione.Length..])
            .ToList();

        var esito = await _email.SalvaAsync(IdAttore, id, scelto, valori, rimozioni, tokenConcorrenza, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Impostazioni email salvate.");
            return RedirectToPage(new { id });
        }

        if (esito.Codice != CodiceEsito.DatiNonValidi)
        {
            MessaggioErrore = esito.Codice == CodiceEsito.ChiaveSegretiMancante
                ? Localizzatore.Traduci("La chiave per cifrare i segreti non è configurata sul server.")
                : TestoErrore(esito.Codice);
            return RedirectToPage(new { id });
        }

        // Dati non validi: si mostra di nuovo il modulo con i valori inseriti (tranne i segreti) e i campi da correggere.
        IdTenant = id;
        Scheda = await _email.LeggiAsync(IdAttore, id, annullamento);
        if (Scheda is null)
        {
            return NotFound();
        }

        Provider = scelto;
        Valori = valori.Where(c => Campi.Any(campo => campo.Nome == c.Key && !campo.Segreto)).ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        SegretiImpostati = Scheda.Impostazioni?.Provider == scelto ? Scheda.Impostazioni.SegretiImpostati : new HashSet<string>();
        TokenConcorrenza = Scheda.Impostazioni?.TokenConcorrenza;
        CampiNonValidi = esito.CampiNonValidi;
        Errore = Localizzatore.Traduci("Controlla i campi evidenziati.");
        return Page();
    }

    public async Task<IActionResult> OnPostProvaAsync(Guid id, string? destinatario, CancellationToken annullamento)
    {
        var messaggio = new MessaggioEmail(
            destinatario ?? string.Empty,
            null,
            Localizzatore.Traduci("Email di prova"),
            Localizzatore.Traduci("Questa è un'email di prova inviata dalla configurazione del tenant. Se la ricevi, l'invio funziona."));
        var esito = await _email.AccodaProvaAsync(IdAttore, id, destinatario, messaggio, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Email di prova accodata: l'invio avviene in background, l'eventuale errore comparirà qui sotto.");
        }
        else
        {
            MessaggioErrore = esito.Codice == CodiceEsito.NonConsentito
                ? Localizzatore.Traduci("Salva prima le impostazioni email del tenant.")
                : TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id });
    }

    public string NomeProvider(ProviderEmail provider) => provider switch
    {
        ProviderEmail.Smtp => Localizzatore.Traduci("SMTP"),
        ProviderEmail.Graph => Localizzatore.Traduci("Microsoft Graph"),
        ProviderEmail.Gmail => Localizzatore.Traduci("Gmail"),
        ProviderEmail.SendGrid => Localizzatore.Traduci("SendGrid"),
        ProviderEmail.MicrosoftOAuth => Localizzatore.Traduci("Microsoft 365 (con consenso)"),
        ProviderEmail.GoogleOAuth => Localizzatore.Traduci("Google Mail (con consenso)"),
        _ => provider.ToString(),
    };

    public string Etichetta(string campo) => campo switch
    {
        "Host" => Localizzatore.Traduci("Server SMTP"),
        "Porta" => Localizzatore.Traduci("Porta"),
        "ModalitaTls" => Localizzatore.Traduci("Sicurezza della connessione"),
        "Utente" => Localizzatore.Traduci("Utente"),
        "Password" => Localizzatore.Traduci("Password"),
        "IndirizzoMittente" => Localizzatore.Traduci("Indirizzo del mittente"),
        "NomeMittente" => Localizzatore.Traduci("Nome del mittente"),
        "TenantMicrosoft" => Localizzatore.Traduci("Tenant Microsoft"),
        "IdClient" => Localizzatore.Traduci("Identificativo client"),
        "SegretoClient" => Localizzatore.Traduci("Segreto del client"),
        "CasellaMittente" => Localizzatore.Traduci("Casella del mittente"),
        "UtenteMittente" => Localizzatore.Traduci("Utente mittente"),
        "CredenzialiJson" => Localizzatore.Traduci("Credenziali (JSON)"),
        "ChiaveApi" => Localizzatore.Traduci("Chiave API"),
        _ => campo,
    };

    public string NomeTls(string modalita) => modalita switch
    {
        DefinizioniProviderEmail.TlsNessuna => Localizzatore.Traduci("Nessuna (solo reti fidate)"),
        DefinizioniProviderEmail.TlsStartTls => Localizzatore.Traduci("STARTTLS (porta 587)"),
        DefinizioniProviderEmail.TlsImmediato => Localizzatore.Traduci("TLS immediato (porta 465)"),
        _ => modalita,
    };

    private void PreparaValori()
    {
        var salvate = Scheda?.Impostazioni;
        if (salvate is not null && salvate.Provider == Provider)
        {
            Valori = salvate.Valori;
            SegretiImpostati = salvate.SegretiImpostati;
        }
        else if (Provider == ProviderEmail.Smtp)
        {
            Valori = new Dictionary<string, string> { ["Porta"] = "587", ["ModalitaTls"] = DefinizioniProviderEmail.TlsStartTls };
        }

        TokenConcorrenza = salvate?.TokenConcorrenza;
    }
}

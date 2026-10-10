namespace DvCms.Web.Middleware;

/// <summary>
/// Intestazioni di sicurezza su ogni risposta (G-005): niente sniffing del tipo, politica del referrer, funzioni del browser
/// negate, isolamento della finestra e una Content-Security-Policy che consente solo risorse della stessa origine (nessuno script
/// né stile in linea di script: solo gli attributi <c>style</c> usati dai temi) e vieta di incorniciare l'admin. Una risposta che
/// imposta già un'intestazione (ad esempio i media, con la propria CSP più stretta) la mantiene.
/// </summary>
public sealed class MiddlewareIntestazioniSicurezza
{
    /// <summary>Politica per il sito pubblico: tutto dalla stessa origine, nessun plugin, nessun <c>base</c> esterno.</summary>
    public const string PoliticaPubblica =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    /// <summary>Come la pubblica, ma l'admin non si incornicia da nessuno.</summary>
    public const string PoliticaAdmin =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private readonly RequestDelegate _successivo;
    private readonly bool _soloSegnalazione;

    public MiddlewareIntestazioniSicurezza(RequestDelegate successivo, IConfiguration configurazione)
    {
        _successivo = successivo;
        _soloSegnalazione = configurazione.GetValue<bool>("Sicurezza:Csp:SoloSegnalazione");
    }

    public Task InvokeAsync(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var admin = contesto.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);
        contesto.Response.OnStarting(() =>
        {
            var intestazioni = contesto.Response.Headers;
            Imposta(intestazioni, "X-Content-Type-Options", "nosniff");
            Imposta(intestazioni, "Referrer-Policy", "strict-origin-when-cross-origin");
            Imposta(intestazioni, "Permissions-Policy", "camera=(), microphone=(), geolocation=(), usb=(), interest-cohort=()");
            Imposta(intestazioni, "Cross-Origin-Opener-Policy", "same-origin");
            Imposta(intestazioni, _soloSegnalazione ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy", admin ? PoliticaAdmin : PoliticaPubblica);

            if (admin)
            {
                // Le schermate dell'admin non vanno nella cache del browser né in quelle condivise (il tasto "indietro" dopo l'uscita).
                // Sovrascrive "SAMEORIGIN" che l'antiforgery aggiunge da solo: l'admin non si incornicia nemmeno dalla stessa origine.
                intestazioni["X-Frame-Options"] = "DENY";
                Imposta(intestazioni, "Cache-Control", "no-store");
            }

            return Task.CompletedTask;
        });

        return _successivo(contesto);
    }

    private static void Imposta(IHeaderDictionary intestazioni, string nome, string valore)
    {
        if (!intestazioni.ContainsKey(nome))
        {
            intestazioni[nome] = valore;
        }
    }
}

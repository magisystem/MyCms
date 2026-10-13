using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Email;

namespace DvCms.Application.Email;

public enum TipoCampoEmail
{
    Testo = 1,
    Numero = 2,
    Email = 3,
    ModalitaTls = 4,
    TestoLungo = 5,

    /// <summary>Identificativo senza spazi né simboli speciali (tenant Microsoft, client id): finisce in indirizzi, quindi si limita l'alfabeto.</summary>
    Identificativo = 6,

    /// <summary>JSON di un account di servizio Google (<c>type</c>, <c>client_email</c>, <c>private_key</c>).</summary>
    CredenzialiGoogle = 7,

    /// <summary>Data nel formato <c>aaaa-mm-gg</c>.</summary>
    Data = 8,
}

/// <summary>Un campo di configurazione di un canale: la UI mostra solo i campi del canale scelto.</summary>
public sealed record CampoProviderEmail(string Nome, string Etichetta, TipoCampoEmail Tipo, bool Obbligatorio, bool Segreto);

/// <summary>Campi e regole di validazione dei quattro canali (ADR-0015). Un solo posto per UI, validazione e adapter.</summary>
public static partial class DefinizioniProviderEmail
{
    /// <summary>Data (facoltativa) in cui scade il segreto del client OAuth: la pagina del tenant avvisa in anticipo (ADR-0021).</summary>
    public const string CampoScadenzaSegreto = "ScadenzaSegretoClient";

    public const string TlsNessuna = "Nessuna";
    public const string TlsStartTls = "StartTls";
    public const string TlsImmediato = "TlsImmediato";

    public static IReadOnlyList<string> ModalitaTls { get; } = [TlsNessuna, TlsStartTls, TlsImmediato];

    private static readonly CampoProviderEmail[] _smtp =
    [
        new("Host", "Server SMTP", TipoCampoEmail.Testo, true, false),
        new("Porta", "Porta", TipoCampoEmail.Numero, true, false),
        new("ModalitaTls", "Sicurezza della connessione", TipoCampoEmail.ModalitaTls, true, false),
        new("Utente", "Utente", TipoCampoEmail.Testo, false, false),
        new("Password", "Password", TipoCampoEmail.Testo, false, true),
        new("IndirizzoMittente", "Indirizzo del mittente", TipoCampoEmail.Email, true, false),
        new("NomeMittente", "Nome del mittente", TipoCampoEmail.Testo, false, false),
    ];

    private static readonly CampoProviderEmail[] _graph =
    [
        new("TenantMicrosoft", "Tenant Microsoft", TipoCampoEmail.Identificativo, true, false),
        new("IdClient", "Identificativo client", TipoCampoEmail.Identificativo, true, false),
        new("SegretoClient", "Segreto del client", TipoCampoEmail.Testo, true, true),
        new("CasellaMittente", "Casella del mittente", TipoCampoEmail.Email, true, false),
    ];

    private static readonly CampoProviderEmail[] _gmail =
    [
        new("UtenteMittente", "Utente mittente", TipoCampoEmail.Email, true, false),
        new("CredenzialiJson", "Credenziali (JSON)", TipoCampoEmail.CredenzialiGoogle, true, true),
    ];

    private static readonly CampoProviderEmail[] _sendGrid =
    [
        new("ChiaveApi", "Chiave API", TipoCampoEmail.Testo, true, true),
        new("IndirizzoMittente", "Indirizzo del mittente", TipoCampoEmail.Email, true, false),
        new("NomeMittente", "Nome del mittente", TipoCampoEmail.Testo, false, false),
    ];

    /// <summary>App OAuth del tenant per Microsoft 365: registrata in Microsoft Entra dal <c>TenantAdmin</c> (ADR-0021).</summary>
    private static readonly CampoProviderEmail[] _microsoftOAuth =
    [
        new("TenantMicrosoft", "Tenant Microsoft", TipoCampoEmail.Identificativo, true, false),
        new("IdClient", "Identificativo client", TipoCampoEmail.Identificativo, true, false),
        new("SegretoClient", "Segreto del client", TipoCampoEmail.Testo, true, true),
        new(CampoScadenzaSegreto, "Scadenza del segreto del client", TipoCampoEmail.Data, false, false),
    ];

    /// <summary>App OAuth del tenant per Google: creata nella Google Cloud Console dal <c>TenantAdmin</c> (ADR-0021).</summary>
    private static readonly CampoProviderEmail[] _googleOAuth =
    [
        new("IdClient", "Identificativo client", TipoCampoEmail.Identificativo, true, false),
        new("SegretoClient", "Segreto del client", TipoCampoEmail.Testo, true, true),
        new(CampoScadenzaSegreto, "Scadenza del segreto del client", TipoCampoEmail.Data, false, false),
    ];

    public static IReadOnlyList<CampoProviderEmail> Campi(ProviderEmail provider) => provider switch
    {
        ProviderEmail.Smtp => _smtp,
        ProviderEmail.Graph => _graph,
        ProviderEmail.Gmail => _gmail,
        ProviderEmail.SendGrid => _sendGrid,
        ProviderEmail.MicrosoftOAuth => _microsoftOAuth,
        ProviderEmail.GoogleOAuth => _googleOAuth,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider email sconosciuto."),
    };

    /// <summary>
    /// Controlla i valori rispetto ai campi del canale. Restituisce i nomi dei campi non validi o mancanti (mai i valori).
    /// Campi non previsti dal canale sono segnalati come non validi: non si accettano valori in più.
    /// </summary>
    public static IReadOnlyList<string> Valida(ProviderEmail provider, IReadOnlyDictionary<string, string> valori)
    {
        ArgumentNullException.ThrowIfNull(valori);
        if (!Enum.IsDefined(provider))
        {
            return ["Provider"];
        }

        var campi = Campi(provider);
        var errori = new List<string>();
        foreach (var nome in valori.Keys.Where(chiave => campi.All(campo => campo.Nome != chiave)))
        {
            errori.Add(nome.Length > 50 ? nome[..50] : nome);
        }

        foreach (var campo in campi)
        {
            valori.TryGetValue(campo.Nome, out var grezzo);
            var valore = grezzo?.Trim();
            if (string.IsNullOrEmpty(valore))
            {
                if (campo.Obbligatorio)
                {
                    errori.Add(campo.Nome);
                }

                continue;
            }

            if (valore.Length > 8000 || (campo.Tipo is not (TipoCampoEmail.TestoLungo or TipoCampoEmail.CredenzialiGoogle) && valore.Any(char.IsControl)) || !TipoValido(campo.Tipo, valore))
            {
                errori.Add(campo.Nome);
            }
        }

        return errori;
    }

    private static bool TipoValido(TipoCampoEmail tipo, string valore) => tipo switch
    {
        TipoCampoEmail.Numero => int.TryParse(valore, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var numero) && numero is >= 1 and <= 65535,
        TipoCampoEmail.Email => NormalizzatoreEmail.TentaNormalizza(valore, out _),
        TipoCampoEmail.ModalitaTls => ModalitaTls.Contains(valore),
        TipoCampoEmail.Identificativo => Identificativo().IsMatch(valore),
        TipoCampoEmail.CredenzialiGoogle => CredenzialiGoogleValide(valore),
        TipoCampoEmail.Data => TentaLeggereData(valore, out _),
        _ => true,
    };

    /// <summary>Legge una data <c>aaaa-mm-gg</c> (solo quel formato).</summary>
    public static bool TentaLeggereData(string? valore, out DateOnly data) =>
        DateOnly.TryParseExact(valore?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out data);

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex Identificativo();

    private static bool CredenzialiGoogleValide(string valore)
    {
        try
        {
            using var documento = System.Text.Json.JsonDocument.Parse(valore);
            var radice = documento.RootElement;
            return radice.ValueKind == System.Text.Json.JsonValueKind.Object
                && radice.TryGetProperty("type", out var tipo) && tipo.GetString() == "service_account"
                && radice.TryGetProperty("client_email", out var email) && email.ValueKind == System.Text.Json.JsonValueKind.String && NormalizzatoreEmail.TentaNormalizza(email.GetString(), out _)
                && radice.TryGetProperty("private_key", out var chiave) && chiave.ValueKind == System.Text.Json.JsonValueKind.String && chiave.GetString()!.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

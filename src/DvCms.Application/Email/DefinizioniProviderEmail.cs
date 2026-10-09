using DvCms.Domain.Accessi;
using DvCms.Domain.Email;

namespace DvCms.Application.Email;

public enum TipoCampoEmail
{
    Testo = 1,
    Numero = 2,
    Email = 3,
    ModalitaTls = 4,
    TestoLungo = 5,
}

/// <summary>Un campo di configurazione di un canale: la UI mostra solo i campi del canale scelto.</summary>
public sealed record CampoProviderEmail(string Nome, string Etichetta, TipoCampoEmail Tipo, bool Obbligatorio, bool Segreto);

/// <summary>Campi e regole di validazione dei quattro canali (ADR-0015). Un solo posto per UI, validazione e adapter.</summary>
public static class DefinizioniProviderEmail
{
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
        new("TenantMicrosoft", "Tenant Microsoft", TipoCampoEmail.Testo, true, false),
        new("IdClient", "Identificativo client", TipoCampoEmail.Testo, true, false),
        new("SegretoClient", "Segreto del client", TipoCampoEmail.Testo, true, true),
        new("CasellaMittente", "Casella del mittente", TipoCampoEmail.Email, true, false),
    ];

    private static readonly CampoProviderEmail[] _gmail =
    [
        new("UtenteMittente", "Utente mittente", TipoCampoEmail.Email, true, false),
        new("CredenzialiJson", "Credenziali (JSON)", TipoCampoEmail.TestoLungo, true, true),
    ];

    private static readonly CampoProviderEmail[] _sendGrid =
    [
        new("ChiaveApi", "Chiave API", TipoCampoEmail.Testo, true, true),
        new("IndirizzoMittente", "Indirizzo del mittente", TipoCampoEmail.Email, true, false),
        new("NomeMittente", "Nome del mittente", TipoCampoEmail.Testo, false, false),
    ];

    public static IReadOnlyList<CampoProviderEmail> Campi(ProviderEmail provider) => provider switch
    {
        ProviderEmail.Smtp => _smtp,
        ProviderEmail.Graph => _graph,
        ProviderEmail.Gmail => _gmail,
        ProviderEmail.SendGrid => _sendGrid,
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

            if (valore.Length > 8000 || (campo.Tipo != TipoCampoEmail.TestoLungo && valore.Any(char.IsControl)) || !TipoValido(campo.Tipo, valore))
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
        _ => true,
    };
}

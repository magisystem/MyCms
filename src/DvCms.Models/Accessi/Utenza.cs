using DvCms.Extender;

namespace DvCms.Models.Accessi;

/// <summary>
/// Account locale (ADR-0016). Il <see cref="RuoloUtenza.PlatformAdmin"/> è globale (senza tenant);
/// <see cref="RuoloUtenza.TenantAdmin"/> e <see cref="RuoloUtenza.Utente"/> appartengono a un solo tenant.
/// L'email è unica nel tenant e può ripetersi in tenant diversi.
/// </summary>
public class Utenza
{
    public Guid Id { get; set; }

    /// <summary>Tenant dell'account; nullo solo per il <see cref="RuoloUtenza.PlatformAdmin"/>.</summary>
    public Guid? IdTenant { get; set; }

    public RuoloUtenza Ruolo { get; set; }

    public string Email { get; set; } = string.Empty;

    public string EmailNormalizzata { get; set; } = string.Empty;

    public string NomeVisualizzato { get; set; } = string.Empty;

    public string HashPassword { get; set; } = string.Empty;

    public DateTime PasswordAggiornataIlUtc { get; set; }

    /// <summary>Vero se l'account ha una password temporanea da cambiare al primo accesso.</summary>
    public bool DeveCambiarePassword { get; set; }

    /// <summary>Cambia a ogni modifica di credenziali o disabilitazione: invalida le sessioni esistenti.</summary>
    public Guid TimbroSicurezza { get; set; }

    public StatoUtenza Stato { get; set; } = StatoUtenza.Attiva;

    public int ContatoreAccessiFalliti { get; set; }

    public DateTime? BloccatoFinoAlUtc { get; set; }

    public DateTime? UltimoAccessoIlUtc { get; set; }

    /// <summary>Lingua preferita nell'interfaccia; nulla significa italiano (lingua di base).</summary>
    public Guid? IdLingua { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    /// <summary>Segreto TOTP attivo, cifrato con la chiave dei segreti (ADR-0019); nullo se la 2FA non è attiva.</summary>
    public string? SegretoTotpProtetto { get; set; }

    /// <summary>Segreto generato ma non ancora confermato con un codice: non vale come secondo fattore.</summary>
    public string? SegretoTotpInAttesaProtetto { get; set; }

    public bool DueFattoriAttivi { get; set; }

    public DateTime? DueFattoriAttivatiIlUtc { get; set; }

    /// <summary>Ultimo passo TOTP accettato: un codice è valido solo se il suo passo è maggiore (anti-replay).</summary>
    public long UltimoPassoTotp { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    public static Utenza Crea(
        RuoloUtenza ruolo,
        Guid? idTenant,
        string email,
        string nomeVisualizzato,
        string hashPassword,
        bool deveCambiarePassword,
        TimeProvider orologio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nomeVisualizzato);
        ArgumentException.ThrowIfNullOrWhiteSpace(hashPassword);
        ArgumentNullException.ThrowIfNull(orologio);

        if (ruolo == RuoloUtenza.PlatformAdmin && idTenant is not null)
        {
            throw new ArgumentException("Il PlatformAdmin è globale e non appartiene a un tenant.", nameof(idTenant));
        }

        if (ruolo != RuoloUtenza.PlatformAdmin && idTenant is null)
        {
            throw new ArgumentException("TenantAdmin e Utente appartengono a un tenant.", nameof(idTenant));
        }

        if (!NormalizzatoreEmail.TentaNormalizza(email, out var normalizzata))
        {
            throw new ArgumentException("Indirizzo email non valido.", nameof(email));
        }

        var adesso = orologio.GetUtcNow().UtcDateTime;
        return new Utenza
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = idTenant,
            Ruolo = ruolo,
            Email = email.Trim(),
            EmailNormalizzata = normalizzata,
            NomeVisualizzato = nomeVisualizzato.Trim(),
            HashPassword = hashPassword,
            PasswordAggiornataIlUtc = adesso,
            DeveCambiarePassword = deveCambiarePassword,
            TimbroSicurezza = Guid.NewGuid(),
            Stato = StatoUtenza.Attiva,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };
    }
}

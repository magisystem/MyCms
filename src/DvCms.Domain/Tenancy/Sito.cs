namespace DvCms.Domain.Tenancy;

/// <summary>Sito di un tenant. Un tenant può avere più siti.</summary>
public class Sito
{
    public const string FusoOrarioPredefinito = "Europe/Rome";

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public string Nome { get; set; } = string.Empty;

    /// <summary>Fuso orario IANA del sito, usato solo per la presentazione (gli istanti restano UTC).</summary>
    public string FusoOrario { get; set; } = FusoOrarioPredefinito;

    /// <summary>Il sito ha il negozio (catalogo, carrello, ordini). Lo imposta il <c>PlatformAdmin</c>; default falso.</summary>
    public bool CommercioAttivo { get; set; }

    /// <summary>Abilita la registrazione degli acquirenti; vero solo se l'e-commerce è attivo (default falso).</summary>
    public bool RegistrazioneAcquirentiAbilitata { get; set; }

    public StatoSito Stato { get; set; } = StatoSito.Attivo;

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    public static Sito Crea(Guid idTenant, string nome, string? fusoOrario, TimeProvider orologio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentNullException.ThrowIfNull(orologio);

        var adesso = orologio.GetUtcNow().UtcDateTime;
        return new Sito
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = idTenant,
            Nome = nome.Trim(),
            FusoOrario = string.IsNullOrWhiteSpace(fusoOrario) ? FusoOrarioPredefinito : fusoOrario.Trim(),
            Stato = StatoSito.Attivo,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };
    }
}

namespace DvCms.Domain.Tenancy;

/// <summary>Cliente/organizzazione contrattuale della piattaforma. Il tenant primario (di gestione) è uno solo.</summary>
public class Tenant
{
    public Guid Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public StatoTenant Stato { get; set; } = StatoTenant.Attivo;

    /// <summary>Tenant primario (di gestione dell'operatore): ne esiste uno solo, creato dal seeding.</summary>
    public bool Primario { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    public static Tenant Crea(string nome, bool primario, TimeProvider orologio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentNullException.ThrowIfNull(orologio);

        var adesso = orologio.GetUtcNow().UtcDateTime;
        return new Tenant
        {
            Id = Identificativi.Nuovo(orologio),
            Nome = nome.Trim(),
            Primario = primario,
            Stato = StatoTenant.Attivo,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };
    }
}

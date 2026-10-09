namespace DvCms.Domain.Tenancy;

/// <summary>
/// Host registrato di un sito. La verifica del possesso del dominio è manuale (ADR-0014): chi registra il dominio
/// lo dichiara verificato, e restano traccia di chi e quando. Un host risolve un sito solo se è attivo e verificato.
/// </summary>
public class DominioSito
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    /// <summary>Host normalizzato (<see cref="NormalizzatoreHost"/>), unico in tutta la piattaforma.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Dominio valido solo in ambiente di sviluppo (ad esempio <c>localhost</c>).</summary>
    public bool SoloSviluppo { get; set; }

    public bool Attivo { get; set; } = true;

    public DateTime? VerificatoIlUtc { get; set; }

    public Guid? VerificatoDaIdUtenza { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    /// <summary>Crea un dominio già verificato dall'attore indicato (nullo per il seeding di sistema).</summary>
    public static DominioSito CreaVerificato(
        Guid idTenant,
        Guid idSito,
        string host,
        bool soloSviluppo,
        Guid? verificatoDaIdUtenza,
        TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(orologio);
        if (!NormalizzatoreHost.TentaNormalizza(host, out var normalizzato))
        {
            throw new ArgumentException($"Host non valido: {NormalizzatoreHost.PerMessaggio(host)}", nameof(host));
        }

        var adesso = orologio.GetUtcNow().UtcDateTime;
        return new DominioSito
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = idTenant,
            IdSito = idSito,
            Host = normalizzato,
            SoloSviluppo = soloSviluppo,
            Attivo = true,
            VerificatoIlUtc = adesso,
            VerificatoDaIdUtenza = verificatoDaIdUtenza,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };
    }
}

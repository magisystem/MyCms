namespace DvCms.Domain.Cms;

/// <summary>
/// Versione immutabile del contenuto di una pagina. In M1 il contenuto è testo semplice (titolo e paragrafi);
/// il documento del builder e l'HTML ripulito arrivano con le milestone successive, aggiungendo colonne.
/// </summary>
public class RevisionePagina
{
    public const int LunghezzaMassimaTitolo = 200;
    public const int LunghezzaMassimaContenuto = 100_000;
    public const int LunghezzaMassimaNota = 500;
    public const int VersioneSchemaTestoSemplice = 1;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdPagina { get; set; }

    public int NumeroRevisione { get; set; }

    public int VersioneSchema { get; set; } = VersioneSchemaTestoSemplice;

    public string Titolo { get; set; } = string.Empty;

    /// <summary>Testo semplice: i paragrafi sono separati da una riga vuota. Non è HTML e viene sempre codificato in uscita.</summary>
    public string Contenuto { get; set; } = string.Empty;

    public string? NotaRevisione { get; set; }

    public Guid CreatoDaIdUtenza { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public static bool TentaValidaTesto(string? titolo, string? contenuto, out string titoloValido, out string contenutoValido)
    {
        titoloValido = titolo?.Trim() ?? string.Empty;
        contenutoValido = contenuto?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() ?? string.Empty;
        return titoloValido.Length is > 0 and <= LunghezzaMassimaTitolo
            && !titoloValido.Any(char.IsControl)
            && contenutoValido.Length <= LunghezzaMassimaContenuto
            && !contenutoValido.Any(carattere => char.IsControl(carattere) && carattere != '\n' && carattere != '\t');
    }

    internal static RevisionePagina Crea(
        Pagina pagina,
        int numero,
        string titolo,
        string contenuto,
        string? nota,
        Guid idAutore,
        DateTime adesso,
        TimeProvider orologio)
    {
        if (!TentaValidaTesto(titolo, contenuto, out var titoloValido, out var contenutoValido))
        {
            throw new ArgumentException("Titolo o contenuto non validi.", nameof(titolo));
        }

        var notaValida = nota?.Trim();
        if (notaValida is { Length: > LunghezzaMassimaNota })
        {
            notaValida = notaValida[..LunghezzaMassimaNota];
        }

        return new RevisionePagina
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = pagina.IdTenant,
            IdSito = pagina.IdSito,
            IdPagina = pagina.Id,
            NumeroRevisione = numero,
            Titolo = titoloValido,
            Contenuto = contenutoValido,
            NotaRevisione = string.IsNullOrEmpty(notaValida) ? null : notaValida,
            CreatoDaIdUtenza = idAutore,
            CreatoIlUtc = adesso,
        };
    }
}

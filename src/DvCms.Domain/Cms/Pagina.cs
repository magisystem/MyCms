namespace DvCms.Domain.Cms;

/// <summary>
/// Pagina di un sito, in una lingua. Bozza e versione pubblicata sono separate: ogni salvataggio crea una nuova
/// <see cref="RevisionePagina"/> immutabile e sposta <see cref="IdUltimaRevisioneBozza"/>; solo la pubblicazione sposta
/// <see cref="IdRevisionePubblicata"/>, che è l'unica versione visibile al pubblico.
/// </summary>
public class Pagina
{
    public const string TipoStandard = "Standard";

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdLingua { get; set; }

    /// <summary>Le versioni nelle varie lingue della stessa pagina condividono il gruppo.</summary>
    public Guid IdGruppoTraduzione { get; set; }

    /// <summary>Slug normalizzato (<see cref="NormalizzatoreSlug"/>), unico per sito e lingua.</summary>
    public string Slug { get; set; } = string.Empty;

    public string TipoPagina { get; set; } = TipoStandard;

    public StatoPagina Stato { get; set; } = StatoPagina.Bozza;

    /// <summary>Pagina servita alla radice del sito (<c>/</c>) per la sua lingua; al più una per sito e lingua.</summary>
    public bool PaginaIniziale { get; set; }

    public Guid? IdUltimaRevisioneBozza { get; set; }

    public Guid? IdRevisionePubblicata { get; set; }

    /// <summary>Ultimo numero di revisione assegnato; la numerazione è protetta dal token di concorrenza.</summary>
    public int UltimoNumeroRevisione { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public DateTime? PubblicatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    /// <summary>Crea la pagina con la sua prima revisione (bozza), con il modello predefinito.</summary>
    public static (Pagina Pagina, RevisionePagina Revisione) Crea(
        Guid idTenant,
        Guid idSito,
        Guid idLingua,
        string slug,
        string titolo,
        string contenuto,
        bool paginaIniziale,
        Guid idAutore,
        TimeProvider orologio) =>
        Crea(idTenant, idSito, idLingua, slug, new ContenutoRevisione(titolo, contenuto), paginaIniziale, idAutore, orologio);

    /// <summary>Crea la pagina con la sua prima revisione (bozza).</summary>
    public static (Pagina Pagina, RevisionePagina Revisione) Crea(
        Guid idTenant,
        Guid idSito,
        Guid idLingua,
        string slug,
        ContenutoRevisione contenuto,
        bool paginaIniziale,
        Guid idAutore,
        TimeProvider orologio,
        Guid? idGruppoTraduzione = null)
    {
        ArgumentNullException.ThrowIfNull(orologio);
        if (!NormalizzatoreSlug.TentaNormalizza(slug, out var slugNormalizzato))
        {
            throw new ArgumentException("Slug non valido o riservato.", nameof(slug));
        }

        var adesso = orologio.GetUtcNow().UtcDateTime;
        var pagina = new Pagina
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = idTenant,
            IdSito = idSito,
            IdLingua = idLingua,
            IdGruppoTraduzione = idGruppoTraduzione ?? Identificativi.Nuovo(orologio),
            Slug = slugNormalizzato,
            PaginaIniziale = paginaIniziale,
            Stato = StatoPagina.Bozza,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };
        var revisione = pagina.AggiungiRevisione(contenuto, "Creazione", idAutore, orologio);
        return (pagina, revisione);
    }

    /// <summary>Aggiunge una revisione (nuova bozza) e la rende l'ultima. Le revisioni esistenti non cambiano mai.</summary>
    public RevisionePagina AggiungiRevisione(string titolo, string contenuto, string? nota, Guid idAutore, TimeProvider orologio) =>
        AggiungiRevisione(new ContenutoRevisione(titolo, contenuto), nota, idAutore, orologio);

    /// <summary>Aggiunge una revisione (nuova bozza) e la rende l'ultima. Le revisioni esistenti non cambiano mai.</summary>
    public RevisionePagina AggiungiRevisione(ContenutoRevisione contenuto, string? nota, Guid idAutore, TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(orologio);

        var adesso = orologio.GetUtcNow().UtcDateTime;
        var revisione = RevisionePagina.Crea(this, UltimoNumeroRevisione + 1, contenuto, nota, idAutore, adesso, orologio);
        UltimoNumeroRevisione = revisione.NumeroRevisione;
        IdUltimaRevisioneBozza = revisione.Id;
        AggiornatoIlUtc = adesso;
        return revisione;
    }

    /// <summary>Rende pubblica la revisione indicata, che deve appartenere a questa pagina.</summary>
    public void Pubblica(RevisionePagina revisione, TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(revisione);
        ArgumentNullException.ThrowIfNull(orologio);

        if (revisione.IdPagina != Id || revisione.IdTenant != IdTenant)
        {
            throw new ArgumentException("La revisione non appartiene a questa pagina.", nameof(revisione));
        }

        var adesso = orologio.GetUtcNow().UtcDateTime;
        IdRevisionePubblicata = revisione.Id;
        Stato = StatoPagina.Pubblicata;
        PubblicatoIlUtc = adesso;
        AggiornatoIlUtc = adesso;
    }

    /// <summary>Toglie la pagina dal pubblico; le revisioni restano.</summary>
    public void Ritira(TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(orologio);

        IdRevisionePubblicata = null;
        Stato = StatoPagina.Bozza;
        PubblicatoIlUtc = null;
        AggiornatoIlUtc = orologio.GetUtcNow().UtcDateTime;
    }
}

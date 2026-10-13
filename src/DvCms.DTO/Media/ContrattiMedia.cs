using DvCms.Models.Media;

namespace DvCms.DTO.Media;

/// <summary>Limiti dei caricamenti (configurazione <c>Media:*</c>).</summary>
public sealed record OpzioniMedia(long DimensioneMassima, int LatoMassimoImmagine, long PixelMassimi)
{
    public static OpzioniMedia Predefinite { get; } = new(10L * 1024 * 1024, 12_000, 50_000_000);
}

/// <summary>Un file da caricare: il flusso, il nome come lo dichiara il client e il tipo dichiarato (che non si usa mai per decidere).</summary>
public sealed record FileDaCaricare(Stream Contenuto, string? NomeOriginale, string? TipoDichiarato);

public sealed record RiepilogoMedia(
    Guid Id,
    string NomeOriginale,
    string TipoContenuto,
    long Dimensione,
    int? Larghezza,
    int? Altezza,
    VisibilitaMedia Visibilita,
    StatoScansioneMedia StatoScansione,
    DateTime CreatoIlUtc,
    string? TestoAlternativoPredefinito);

public sealed record TestoAlternativoMedia(string CodiceLingua, string NomeLingua, string Testo);

public sealed record DettaglioMedia(RiepilogoMedia Riepilogo, string HashSha256, string TokenConcorrenza, IReadOnlyList<TestoAlternativoMedia> TestiAlternativi);

/// <summary>Un file pronto per essere servito. Chi lo riceve chiude il flusso.</summary>
public sealed record ContenutoMedia(Stream Flusso, string TipoContenuto, long Dimensione, string Etag, string NomeOriginale);

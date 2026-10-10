using DvCms.Application.Piattaforma;
using DvCms.Domain.Media;

namespace DvCms.Application.Media;

/// <summary>
/// Archivio degli oggetti (D-003): conserva i byte sotto una chiave. Non conosce tenant né autorizzazioni: l'isolamento è
/// compito di <see cref="IServizioMedia"/>, che genera le chiavi e controlla la proprietà prima di leggere o scrivere.
/// Gli oggetti sono immutabili: scrivere su una chiave esistente è un errore.
/// </summary>
public interface IArchivioOggetti
{
    /// <exception cref="ArgumentException">La chiave non ha il formato ammesso.</exception>
    /// <exception cref="InvalidOperationException">L'oggetto esiste già.</exception>
    Task ScriviAsync(string chiave, Stream contenuto, CancellationToken annullamento);

    /// <summary>Apre l'oggetto in lettura, o restituisce nullo se non esiste. Chi riceve il flusso lo chiude.</summary>
    Task<Stream?> ApriAsync(string chiave, CancellationToken annullamento);

    /// <summary>Elimina l'oggetto; vero se esisteva.</summary>
    Task<bool> EliminaAsync(string chiave, CancellationToken annullamento);

    /// <summary>Le chiavi di tutti gli oggetti presenti (per verifiche di integrità e ripristini; non per il servizio delle richieste).</summary>
    IAsyncEnumerable<string> ElencaChiaviAsync(CancellationToken annullamento);
}

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

/// <summary>
/// Libreria media del sito dell'host (D-003, D-004). Gestione riservata agli amministratori del tenant (riverificati sul
/// database a ogni operazione); la lettura pubblica riguarda solo le risorse pubbliche del sito dell'host.
/// </summary>
public interface IServizioMedia
{
    Task<IReadOnlyList<RiepilogoMedia>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    Task<DettaglioMedia?> LeggiAsync(Guid idAttore, Guid idRisorsa, CancellationToken annullamento);

    /// <summary>
    /// Verifica il file (dimensione, estensione ammessa, firma coerente con l'estensione, dimensioni dell'immagine), lo salva
    /// nell'archivio e ne registra i metadati. In caso di errore non resta nulla: l'esito dice cosa non andava
    /// (<c>File</c>, <c>Dimensione</c>, <c>Tipo</c>).
    /// </summary>
    Task<EsitoOperazione> CaricaAsync(Guid idAttore, FileDaCaricare file, CancellationToken annullamento);

    /// <summary>Imposta il testo alternativo in una lingua abilitata del sito; un testo vuoto lo toglie.</summary>
    Task<EsitoOperazione> ImpostaTestoAlternativoAsync(Guid idAttore, Guid idRisorsa, string? codiceLingua, string? testo, CancellationToken annullamento);

    Task<EsitoOperazione> ImpostaVisibilitaAsync(Guid idAttore, Guid idRisorsa, VisibilitaMedia visibilita, CancellationToken annullamento);

    Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idRisorsa, CancellationToken annullamento);

    /// <summary>Apre una risorsa per un amministratore del tenant, anche se privata.</summary>
    Task<ContenutoMedia?> ApriPerAmministratoreAsync(Guid idAttore, Guid idRisorsa, CancellationToken annullamento);

    /// <summary>Apre una risorsa pubblica del sito dell'host; nullo se è privata, in quarantena, inesistente o di un altro sito.</summary>
    Task<ContenutoMedia?> ApriPubblicaAsync(Guid idRisorsa, CancellationToken annullamento);
}

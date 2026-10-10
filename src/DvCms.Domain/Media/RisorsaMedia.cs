namespace DvCms.Domain.Media;

public enum VisibilitaMedia
{
    /// <summary>Servita a chiunque dal dominio del sito (le immagini delle pagine pubbliche).</summary>
    Pubblica = 1,

    /// <summary>Solo agli amministratori del tenant, dall'area di amministrazione.</summary>
    Privata = 2,
}

public enum StatoScansioneMedia
{
    /// <summary>Nessuna scansione antimalware eseguita (ancora non prevista: vedi G-005).</summary>
    NonEseguita = 1,

    Pulito = 2,

    /// <summary>Il file non si serve a nessuno finché non viene rivisto.</summary>
    InQuarantena = 3,
}

/// <summary>
/// Un file caricato in un sito (D-003, D-004). Il binario sta nell'archivio degli oggetti, sotto una chiave generata dal
/// server; qui ci sono i metadati verificati dal server: tipo rilevato dalla firma del file (mai quello dichiarato dal
/// client), dimensione, impronta SHA-256 e, per le immagini, larghezza e altezza.
/// </summary>
public class RisorsaMedia
{
    public const int LunghezzaMassimaNome = 200;
    public const int LunghezzaMassimaTestoAlternativo = 500;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    /// <summary>Chiave dell'oggetto nell'archivio: <c>{tenant}/{sito}/{risorsa}</c>, generata dal server, mai dal client.</summary>
    public string ChiaveOggetto { get; set; } = string.Empty;

    /// <summary>Nome del file come caricato, ripulito (niente percorsi né caratteri di controllo): serve solo da etichetta.</summary>
    public string NomeOriginale { get; set; } = string.Empty;

    /// <summary>Tipo rilevato dalla firma del file (ad esempio <c>image/png</c>).</summary>
    public string TipoContenuto { get; set; } = string.Empty;

    public long Dimensione { get; set; }

    public byte[] HashSha256 { get; set; } = [];

    public int? Larghezza { get; set; }

    public int? Altezza { get; set; }

    public VisibilitaMedia Visibilita { get; set; } = VisibilitaMedia.Pubblica;

    public StatoScansioneMedia StatoScansione { get; set; } = StatoScansioneMedia.NonEseguita;

    public DateTime CreatoIlUtc { get; set; }

    public Guid? CreatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    public static string CreaChiave(Guid idTenant, Guid idSito, Guid idRisorsa) => $"{idTenant:N}/{idSito:N}/{idRisorsa:N}";
}

/// <summary>Testo alternativo di una risorsa in una lingua (ADR-0018: i testi editabili hanno una tabella di traduzioni).</summary>
public class TraduzioneRisorsaMedia
{
    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdRisorsa { get; set; }

    public Guid IdLingua { get; set; }

    public string TestoAlternativo { get; set; } = string.Empty;
}

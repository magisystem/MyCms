namespace DvCms.Models.Moduli;

/// <summary>Stato di pubblicazione di un modulo di contatto (ADR-0027). Solo un modulo <c>Attivo</c> accetta invii.</summary>
public enum StatoModulo
{
    /// <summary>In preparazione: non accetta invii e può essere incompleto.</summary>
    Bozza = 1,

    /// <summary>Pubblicabile in una pagina e pronto a ricevere invii.</summary>
    Attivo = 2,

    /// <summary>Non accetta più invii; le richieste ricevute restano consultabili fino alla scadenza.</summary>
    Archiviato = 3,
}

/// <summary>
/// Modulo di contatto personalizzato di un sito (ADR-0027). I campi, i testi e le regole sono dati scelti dal <c>TenantAdmin</c> fra tipi
/// predefiniti: la definizione sta in <see cref="DefinizioneJson"/>, le impostazioni di notifica in <see cref="ImpostazioniJson"/>.
/// </summary>
public class Modulo
{
    public const int NumeroMassimoPerSito = 50;
    public const int LunghezzaMassimaNome = 100;
    public const int GiorniConservazioneMinimi = 30;
    public const int GiorniConservazionePredefiniti = 365;
    public const int GiorniConservazioneMassimi = 1825;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    /// <summary>Nome interno, unico nel sito: serve a scegliere il modulo nell'editor e a riconoscerlo nell'elenco.</summary>
    public string Nome { get; set; } = string.Empty;

    public StatoModulo Stato { get; set; }

    /// <summary>Campi e testi per lingua (JSON, schema in <c>DefinizioneModulo</c>).</summary>
    public string DefinizioneJson { get; set; } = "{}";

    /// <summary>Destinatari della notifica e risposta automatica (JSON, schema in <c>ImpostazioniModulo</c>).</summary>
    public string ImpostazioniJson { get; set; } = "{}";

    /// <summary>Giorni dopo i quali le richieste si cancellano da sole.</summary>
    public int GiorniConservazione { get; set; } = GiorniConservazionePredefiniti;

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

public enum StatoInvioModulo
{
    Nuovo = 1,
    Letto = 2,
    Archiviato = 3,
}

/// <summary>
/// Richiesta ricevuta da un modulo (ADR-0027). Contiene <b>dati personali</b> del visitatore: si conserva per il periodo del modulo e
/// non si registrano indirizzo IP né User-Agent. <see cref="ContenutoJson"/> è un elenco autodescrittivo (identificativo, etichetta
/// nella lingua dell'invio, tipo e valore di ogni campo), così le richieste restano leggibili anche se il modulo cambia.
/// </summary>
public class InvioModulo
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdModulo { get; set; }

    public string CodiceLingua { get; set; } = string.Empty;

    public StatoInvioModulo Stato { get; set; }

    public string ContenutoJson { get; set; } = "[]";

    public DateTime CreatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

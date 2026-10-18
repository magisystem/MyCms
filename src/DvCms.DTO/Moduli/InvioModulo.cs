
namespace DvCms.DTO.Moduli;

/// <summary>
/// Un valore inviato, autodescrittivo: l'etichetta è quella nella lingua dell'invio e <paramref name="Valori"/> contiene il testo
/// scritto (una voce) oppure le etichette delle opzioni scelte; per il consenso la voce è <c>si</c>.
/// </summary>
public sealed record ValoreInviato(string Id, string Etichetta, TipoCampoModulo Tipo, IReadOnlyList<string> Valori);

/// <summary>Perché un campo è stato rifiutato: il testo per il visitatore lo sceglie chi mostra la pagina.</summary>
public enum ErroreCampoModulo
{
    Obbligatorio = 1,
    TroppoLungo = 2,
    NonValido = 3,
}

/// <summary>Esito della lettura dei valori ricevuti: errori per campo (e valori da rimostrare), oppure il contenuto da salvare.</summary>
public sealed record EsitoElaborazioneInvio(
    IReadOnlyDictionary<string, ErroreCampoModulo> Errori,
    IReadOnlyDictionary<string, string[]> ValoriRicevuti,
    IReadOnlyList<ValoreInviato>? Contenuto)
{
    public bool Valido => Contenuto is not null;
}

/// <summary>Un modulo pubblicato come lo vede il visitatore: definizione, lingua predefinita del sito e lingua usata.</summary>
public sealed record ModuloPubblico(Guid Id, string Nome, DefinizioneModulo Definizione, string LinguaPredefinita, string Lingua);

public enum EsitoInvioPubblico
{
    Accettato = 1,
    NonTrovato = 2,
    DatiNonValidi = 3,

    /// <summary>Il modulo ha raggiunto il tetto di invii nelle ultime 24 ore.</summary>
    LimiteRaggiunto = 4,
}

/// <summary>Esito di un invio dal sito pubblico. In caso di dati non validi porta errori e valori per rimostrare la form.</summary>
public sealed record RisultatoInvioModulo(
    EsitoInvioPubblico Esito,
    ModuloPubblico? Modulo,
    IReadOnlyDictionary<string, ErroreCampoModulo> Errori,
    IReadOnlyDictionary<string, string[]> ValoriRicevuti)
{
    public static RisultatoInvioModulo Semplice(EsitoInvioPubblico esito) =>
        new(esito, null, new Dictionary<string, ErroreCampoModulo>(), new Dictionary<string, string[]>());
}

/// <summary>Opzioni dei moduli di contatto: tetto di invii nelle ultime 24 ore per ogni modulo (protegge casella e database).</summary>
public sealed record OpzioniModuli(int InviiMassimiAlGiorno)
{
    public const int InviiMassimiPredefiniti = 200;

    public static OpzioniModuli Predefinite { get; } = new(InviiMassimiPredefiniti);
}

using DvCms.DTO.Piattaforma;

namespace DvCms.DTO.Email;

/// <summary>Codici dei modelli email del catalogo (ADR-0025). Chi compone un'email indica uno di questi.</summary>
public static class CodiciModelloEmail
{
    public const string RecuperoCodice = "recupero-codice";
    public const string RecuperoLink = "recupero-link";
    public const string RecuperoNotifica = "recupero-notifica";
    public const string ConfermaRegistrazione = "conferma-registrazione";
    public const string InvitoAmministratore = "invito-amministratore";
    public const string OrdineConfermato = "ordine-confermato";
    public const string OrdineRicevutoBonifico = "ordine-ricevuto-bonifico";
    public const string OrdineRicevutoContrassegno = "ordine-ricevuto-contrassegno";
    public const string OrdineNuovoAmministratore = "ordine-nuovo-amministratore";
    public const string Rimborso = "rimborso";
    public const string Prova = "prova-invio";
}

/// <summary>Una variabile che il modello può usare con <c>{{ nome }}</c>. Se <paramref name="Obbligatoria"/> deve comparire sia nell'HTML sia nel testo.</summary>
public sealed record VariabileModelloEmail(string Nome, string Descrizione, bool Obbligatoria = false);

/// <summary>Un modello del catalogo: cosa è, quando parte e quali variabili offre.</summary>
public sealed record DefinizioneModelloEmail(string Codice, string Nome, string Descrizione, IReadOnlyList<VariabileModelloEmail> Variabili)
{
    /// <summary>Modello critico: ha almeno una variabile obbligatoria (codice o link), senza la quale l'email non serve a nulla.</summary>
    public bool Critico => Variabili.Any(v => v.Obbligatoria);
}

/// <summary>Il contenuto di un modello: oggetto, corpo HTML e alternativa testuale, tutti sorgenti Liquid.</summary>
public sealed record ContenutoModelloEmail(string Oggetto, string CorpoHtml, string CorpoTesto);

/// <summary>Da dove viene il modello usato per comporre: il ripiego scende da sinistra a destra.</summary>
public enum OrigineModelloEmail
{
    /// <summary>Modello del tenant nella lingua richiesta.</summary>
    TenantLingua = 1,

    /// <summary>Modello del tenant nella sua lingua predefinita.</summary>
    TenantLinguaPredefinita = 2,

    /// <summary>Modello di fabbrica nella lingua richiesta.</summary>
    FabbricaLingua = 3,

    /// <summary>Modello di fabbrica in italiano (ultimo ripiego).</summary>
    FabbricaItaliano = 4,
}

/// <summary>Una voce dell'elenco dei modelli del tenant, con le lingue in cui il tenant ha un proprio modello.</summary>
public sealed record VoceModelloEmail(DefinizioneModelloEmail Definizione, IReadOnlyList<string> LinguePersonalizzate);

/// <summary>Il modello di una lingua così come lo vede chi lo modifica.</summary>
public sealed record SchedaModelloEmail(
    DefinizioneModelloEmail Definizione,
    string Lingua,
    ContenutoModelloEmail Contenuto,
    OrigineModelloEmail Origine,
    bool Personalizzato,
    string? TokenConcorrenza,
    IReadOnlyList<string> LingueDisponibili);

/// <summary>Esito della validazione di un contenuto: gli errori sono frasi per chi scrive il modello.</summary>
public sealed record EsitoValidazioneModelloEmail(IReadOnlyList<string> Errori)
{
    public bool Valido => Errori.Count == 0;
}

/// <summary>Il contenuto reso con dati d'esempio. Se non valido non c'è nulla da mostrare.</summary>
public sealed record AnteprimaModelloEmail(EsitoValidazioneModelloEmail Validazione, string Oggetto, string CorpoHtml, string CorpoTesto);

public sealed record EsitoSalvataggioModelloEmail(CodiceEsito Codice, EsitoValidazioneModelloEmail Validazione);

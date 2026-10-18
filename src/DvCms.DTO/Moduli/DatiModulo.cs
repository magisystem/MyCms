using DvCms.DTO.Piattaforma;
using DvCms.Models.Moduli;

namespace DvCms.DTO.Moduli;

/// <summary>Tipi di campo ammessi nei moduli (ADR-0027). Ogni tipo ha regole di validazione sue: non ci sono espressioni regolari scelte dal tenant.</summary>
public enum TipoCampoModulo
{
    TestoBreve = 1,
    TestoLungo = 2,
    Email = 3,
    Telefono = 4,
    Numero = 5,
    Data = 6,
    SceltaSingola = 7,
    SceltaMultipla = 8,
    Consenso = 9,
}

/// <summary>
/// Un campo del modulo. I testi sono per lingua (chiave: codice della lingua del sito); quella predefinita è obbligatoria, le altre
/// ripiegano su di essa. <paramref name="Id"/> è stabile (<c>c1</c>, <c>c2</c>…): non cambia se il campo si rinomina o si sposta.
/// </summary>
public sealed record CampoModulo(
    string Id,
    TipoCampoModulo Tipo,
    bool Obbligatorio,
    int LunghezzaMassima,
    IReadOnlyDictionary<string, string> Etichette,
    IReadOnlyDictionary<string, string> Aiuti,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Opzioni)
{
    public static CampoModulo Nuovo(string id, TipoCampoModulo tipo, string linguaPredefinita) =>
        new(
            id,
            tipo,
            Obbligatorio: tipo == TipoCampoModulo.Consenso,
            LunghezzaPredefinita(tipo),
            new Dictionary<string, string> { [linguaPredefinita] = string.Empty },
            new Dictionary<string, string>(),
            []);

    /// <summary>Lunghezza massima consentita (e predefinita) per un tipo: il valore inviato non la supera mai.</summary>
    public static int LunghezzaPredefinita(TipoCampoModulo tipo) => tipo switch
    {
        TipoCampoModulo.TestoBreve => 200,
        TipoCampoModulo.TestoLungo => 2000,
        TipoCampoModulo.Email => 254,
        TipoCampoModulo.Telefono => 30,
        TipoCampoModulo.Numero => 20,
        TipoCampoModulo.Data => 10,
        _ => 200,
    };

    public static int LunghezzaMassimaConsentita(TipoCampoModulo tipo) => tipo switch
    {
        TipoCampoModulo.TestoBreve => 200,
        TipoCampoModulo.TestoLungo => 5000,
        _ => LunghezzaPredefinita(tipo),
    };

    public bool EScelta => Tipo is TipoCampoModulo.SceltaSingola or TipoCampoModulo.SceltaMultipla;
}

/// <summary>Campi e testi del modulo. Con <see cref="PercorsoDopoInvio"/> vuoto si mostra il messaggio di <see cref="Conferma"/>.</summary>
public sealed record DefinizioneModulo(
    IReadOnlyList<CampoModulo> Campi,
    IReadOnlyDictionary<string, string> Pulsante,
    IReadOnlyDictionary<string, string> Conferma,
    string? PercorsoDopoInvio,
    string? UrlInformativa)
{
    public const int NumeroMassimoCampi = 20;

    public static DefinizioneModulo Vuota(string linguaPredefinita) =>
        new(
            [],
            new Dictionary<string, string> { [linguaPredefinita] = string.Empty },
            new Dictionary<string, string> { [linguaPredefinita] = string.Empty },
            null,
            null);
}

/// <summary>Chi riceve la notifica di una richiesta (fino a cinque indirizzi, oppure tutti i <c>TenantAdmin</c>) e se il visitatore riceve una risposta automatica.</summary>
public sealed record ImpostazioniModulo(
    IReadOnlyList<string> Destinatari,
    bool TuttiGliAmministratori,
    bool RispostaAutomatica,
    int GiorniConservazione)
{
    public const int NumeroMassimoDestinatari = 5;

    public static ImpostazioniModulo Predefinite { get; } = new([], TuttiGliAmministratori: true, RispostaAutomatica: false, Modulo.GiorniConservazionePredefiniti);
}

/// <summary>Dati per creare o aggiornare un modulo.</summary>
public sealed record DatiModulo(string Nome, StatoModulo Stato, DefinizioneModulo Definizione, ImpostazioniModulo Impostazioni);

public sealed record VistaModulo(
    Guid Id,
    string Nome,
    StatoModulo Stato,
    DefinizioneModulo Definizione,
    ImpostazioniModulo Impostazioni,
    string TokenConcorrenza,
    int RichiesteNuove,
    int RichiesteTotali,
    DateTime AggiornatoIlUtc);

public sealed record RiepilogoModulo(Guid Id, string Nome, StatoModulo Stato, int NumeroCampi, int RichiesteNuove, int RichiesteTotali, DateTime AggiornatoIlUtc);

/// <summary>Lingue abilitate nel sito, con la predefinita (le etichette del modulo si scrivono in queste).</summary>
public sealed record LinguaModulo(string Codice, string Nome, bool Predefinita);

/// <summary>Esito di una scrittura: gli errori sono frasi per chi compila il modulo.</summary>
public sealed record EsitoModulo(CodiceEsito Codice, Guid? Id, IReadOnlyList<string> Errori, string? TokenConcorrenza)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

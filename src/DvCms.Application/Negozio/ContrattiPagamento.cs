using DvCms.Application.Piattaforma;
using DvCms.Models.Negozio;

namespace DvCms.Application.Negozio;

/// <summary>I provider di pagamento che il negozio sa usare (ADR-0020, ADR-0022). Il nome è anche quello salvato nei tentativi di pagamento.</summary>
public static class NomiProviderPagamento
{
    public const string PayPal = "PayPal";
    public const string Nexi = "Nexi";

    public static IReadOnlyList<string> Tutti { get; } = [PayPal, Nexi];
}

/// <summary>Credenziali di un provider per un sito. Le classi derivate non mostrano mai i segreti in <see cref="object.ToString"/>.</summary>
public abstract class CredenzialiGateway
{
    protected CredenzialiGateway(AmbientePagamento ambiente) => Ambiente = ambiente;

    public AmbientePagamento Ambiente { get; }
}

/// <summary>Credenziali del terminale Nexi XPay di un sito: la chiave API (<c>X-API-KEY</c>).</summary>
public sealed class CredenzialiNexi : CredenzialiGateway
{
    public CredenzialiNexi(AmbientePagamento ambiente, string chiaveApi)
        : base(ambiente) => ChiaveApi = chiaveApi;

    public string ChiaveApi { get; }

    public override string ToString() => $"Nexi {Ambiente}";
}

/// <summary>Credenziali del conto PayPal di un sito. <see cref="ToString"/> non mostra mai i segreti.</summary>
public sealed class CredenzialiPayPal : CredenzialiGateway
{
    public CredenzialiPayPal(AmbientePagamento ambiente, string idClient, string segretoClient, string idWebhook)
        : base(ambiente)
    {
        IdClient = idClient;
        SegretoClient = segretoClient;
        IdWebhook = idWebhook;
    }

    public string IdClient { get; }

    public string SegretoClient { get; }

    public string IdWebhook { get; }

    public override string ToString() => $"PayPal {Ambiente} (client {IdClient})";
}

public sealed record SchedaPagamentoSito(
    string NomeSito,
    AmbientePagamento Ambiente,
    string IdClient,
    bool SegretoImpostato,
    bool WebhookImpostato,
    string? TokenConcorrenza,
    bool Configurato);

/// <summary>
/// Credenziali PayPal per sito, gestite dal <c>PlatformAdmin</c> con la stessa protezione dei segreti dei canali email (ADR-0015,
/// ADR-0020). I segreti non tornano mai all'interfaccia: si vede solo se sono impostati.
/// </summary>
public interface IServizioImpostazioniPagamento
{
    Task<SchedaPagamentoSito?> LeggiAsync(Guid idAttore, Guid idTenant, Guid idSito, CancellationToken annullamento);

    /// <summary>
    /// Salva ambiente e identificativo client; un segreto lasciato vuoto resta quello già salvato. Esiti: <c>DatiNonValidi</c> (con i
    /// campi), <c>NonTrovato</c>, <c>Conflitto</c>, <c>ChiaveSegretiMancante</c>.
    /// </summary>
    Task<EsitoOperazione> SalvaAsync(
        Guid idAttore,
        Guid idTenant,
        Guid idSito,
        AmbientePagamento ambiente,
        string? idClient,
        string? segretoClient,
        string? idWebhook,
        string? tokenConcorrenza,
        CancellationToken annullamento);
}

public sealed record SchedaNexiSito(string NomeSito, AmbientePagamento Ambiente, bool ChiaveImpostata, string? TokenConcorrenza, bool Configurato);

/// <summary>
/// Credenziali Nexi XPay per sito, gestite dal <c>PlatformAdmin</c> con la stessa protezione dei segreti delle altre (ADR-0022). La chiave API non
/// torna mai all'interfaccia: si vede solo se è impostata.
/// </summary>
public interface IServizioImpostazioniNexi
{
    Task<SchedaNexiSito?> LeggiAsync(Guid idAttore, Guid idTenant, Guid idSito, CancellationToken annullamento);

    /// <summary>Salva ambiente e chiave API (una chiave vuota lascia quella già salvata). Esiti: <c>DatiNonValidi</c>, <c>NonTrovato</c>, <c>Conflitto</c>, <c>ChiaveSegretiMancante</c>.</summary>
    Task<EsitoOperazione> SalvaAsync(Guid idAttore, Guid idTenant, Guid idSito, AmbientePagamento ambiente, string? chiaveApi, string? tokenConcorrenza, CancellationToken annullamento);
}

/// <summary>Lettura delle credenziali per chi deve parlare con il provider (non per l'interfaccia).</summary>
public interface IArchivioCredenzialiPagamento
{
    /// <summary>Le credenziali complete del sito, o <c>null</c> se non sono configurate o non si leggono (chiave dei segreti cambiata).</summary>
    Task<CredenzialiPayPal?> OttieniAsync(Guid idTenant, Guid idSito, CancellationToken annullamento);
}

/// <param name="NumeroOrdine">Numero dell'ordine nel sito (Nexi lo usa per comporre un identificativo breve).</param>
/// <param name="UrlNotifica">Dove il provider invia le notifiche server-to-server (Nexi).</param>
/// <param name="Lingua">Codice lingua del sito dell'ordine (<c>it</c>, <c>en</c>…) per la pagina del provider.</param>
public sealed record RichiestaOrdineProvider(
    Guid IdRichiesta,
    decimal Importo,
    string Valuta,
    string Riferimento,
    string Descrizione,
    Uri UrlRitorno,
    Uri UrlAnnullo,
    int NumeroOrdine = 0,
    Uri? UrlNotifica = null,
    string? EmailCliente = null,
    string? NomeCliente = null,
    string Lingua = "it");

/// <param name="TokenSicurezza">Token che il provider ripeterà nelle notifiche (Nexi): il servizio ne conserva solo l'impronta.</param>
public sealed record OrdineProvider(string IdProvider, Uri UrlApprovazione, string? TokenSicurezza = null)
{
    public override string ToString() => $"OrdineProvider({IdProvider})";
}

public enum StatoCatturaProvider
{
    Completata = 1,
    InSospeso = 2,
    Rifiutata = 3,

    /// <summary>Il cliente non ha (ancora) approvato il pagamento.</summary>
    NonApprovata = 4,
}

public sealed record EsitoCatturaProvider(StatoCatturaProvider Stato, string? IdCattura, decimal? Importo, string? Valuta);

public enum StatoRimborsoProvider
{
    Completato = 1,

    /// <summary>Il provider lo sta elaborando: l'esito arriverà (webhook o rilettura).</summary>
    InSospeso = 2,

    /// <summary>Rifiutato o impossibile (ad esempio importo oltre quanto resta rimborsabile).</summary>
    Fallito = 3,
}

/// <param name="Importo">Importo del rimborso, quando il provider lo comunica (letto per i rimborsi fatti dal suo pannello).</param>
/// <param name="IdCattura">Incasso a cui si riferisce, quando il provider lo comunica.</param>
public sealed record EsitoRimborsoProvider(StatoRimborsoProvider Stato, string? IdRimborso, decimal? Importo = null, string? Valuta = null, string? IdCattura = null);

/// <summary>Un rimborso già eseguito presso il provider (letto dall'ordine, ad esempio uno fatto dal suo pannello).</summary>
public sealed record RimborsoDelProvider(string IdRimborso, decimal Importo, string Valuta);

/// <summary>Intestazioni con cui il provider firma un webhook.</summary>
public sealed record IntestazioniWebhook(string AuthAlgo, string CertUrl, string TransmissionId, string TransmissionSig, string TransmissionTime);

/// <summary>Il provider di pagamento ha rifiutato la richiesta o non ha risposto come previsto. Il messaggio non riporta mai il corpo della risposta né segreti.</summary>
public sealed class ProviderPagamentoException : Exception
{
    public ProviderPagamentoException()
    {
    }

    public ProviderPagamentoException(string message)
        : base(message)
    {
    }

    public ProviderPagamentoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ProviderPagamentoException(string message, int codiceHttp)
        : base(message)
    {
        CodiceHttp = codiceHttp;
    }

    /// <summary>Codice HTTP della risposta del provider, quando l'errore viene da una risposta.</summary>
    public int? CodiceHttp { get; }
}

/// <summary>
/// Il provider di pagamento (PayPal Orders v2, ADR-0020). Importo e valuta li decide il chiamante dal server; le richieste di
/// creazione e cattura portano un identificativo di idempotenza (<c>PayPal-Request-Id</c>) così un ripetersi non incassa due volte.
/// </summary>
public interface IProviderPagamento
{
    Task<OrdineProvider> CreaOrdineAsync(CredenzialiPayPal credenziali, RichiestaOrdineProvider richiesta, CancellationToken annullamento);

    Task<EsitoCatturaProvider> CatturaAsync(CredenzialiPayPal credenziali, string idProvider, Guid idRichiesta, CancellationToken annullamento);

    /// <summary>Stato attuale dell'ordine presso il provider (riconciliazione e cattura già avvenuta).</summary>
    Task<EsitoCatturaProvider> LeggiOrdineAsync(CredenzialiPayPal credenziali, string idProvider, CancellationToken annullamento);

    /// <summary>
    /// Rimborsa (in tutto o in parte) un incasso. <paramref name="idRichiesta"/> è l'identificativo di idempotenza: ripetere la richiesta
    /// dopo una risposta persa non rimborsa due volte.
    /// </summary>
    Task<EsitoRimborsoProvider> RimborsaAsync(CredenzialiPayPal credenziali, string idCattura, decimal importo, string valuta, Guid idRichiesta, string? nota, CancellationToken annullamento);

    /// <summary>Stato attuale di un rimborso presso il provider (riconciliazione dopo un webhook).</summary>
    Task<EsitoRimborsoProvider> LeggiRimborsoAsync(CredenzialiPayPal credenziali, string idRimborso, CancellationToken annullamento);

    /// <summary>Verifica la firma di un webhook con il servizio del provider e l'identificativo del webhook del conto.</summary>
    Task<bool> VerificaWebhookAsync(CredenzialiPayPal credenziali, IntestazioniWebhook intestazioni, string corpo, CancellationToken annullamento);
}

/// <summary>
/// Un provider di pagamento con pagina esterna (PayPal, Nexi XPay; ADR-0022): crea il pagamento, incassa o legge l'esito. Il servizio dei
/// pagamenti sceglie il provider dal nome salvato nel tentativo. Importo e valuta li decide sempre il server.
/// </summary>
public interface IGatewayPagamento
{
    /// <summary>Uno dei <see cref="NomiProviderPagamento"/>.</summary>
    string Nome { get; }

    /// <summary>Le credenziali complete del sito, o <c>null</c> se mancano o non si leggono.</summary>
    Task<CredenzialiGateway?> OttieniCredenzialiAsync(Guid idTenant, Guid idSito, CancellationToken annullamento);

    Task<OrdineProvider> CreaOrdineAsync(CredenzialiGateway credenziali, RichiestaOrdineProvider richiesta, CancellationToken annullamento);

    /// <summary>Al ritorno del cliente: incassa se il provider lo richiede (PayPal) oppure legge l'esito (Nexi, che incassa da sé).</summary>
    Task<EsitoCatturaProvider> CatturaAsync(CredenzialiGateway credenziali, string idProvider, Guid idRichiesta, CancellationToken annullamento);

    /// <summary>Stato attuale presso il provider, senza incassare (riconciliazione, notifiche, incassi orfani).</summary>
    Task<EsitoCatturaProvider> LeggiOrdineAsync(CredenzialiGateway credenziali, string idProvider, CancellationToken annullamento);

    /// <summary>
    /// I rimborsi già eseguiti presso il provider per quel pagamento, completati (anche quelli fatti dal suo pannello). Per PayPal è vuoto:
    /// i suoi rimborsi arrivano uno per uno dai webhook.
    /// </summary>
    Task<IReadOnlyList<RimborsoDelProvider>> LeggiRimborsiAsync(CredenzialiGateway credenziali, string idProvider, CancellationToken annullamento);
}

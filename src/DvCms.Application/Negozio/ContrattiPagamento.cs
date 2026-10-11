using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;

namespace DvCms.Application.Negozio;

/// <summary>Credenziali del conto PayPal di un sito. <see cref="ToString"/> non mostra mai i segreti.</summary>
public sealed class CredenzialiPayPal
{
    public CredenzialiPayPal(AmbientePagamento ambiente, string idClient, string segretoClient, string idWebhook)
    {
        Ambiente = ambiente;
        IdClient = idClient;
        SegretoClient = segretoClient;
        IdWebhook = idWebhook;
    }

    public AmbientePagamento Ambiente { get; }

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

/// <summary>Lettura delle credenziali per chi deve parlare con il provider (non per l'interfaccia).</summary>
public interface IArchivioCredenzialiPagamento
{
    /// <summary>Le credenziali complete del sito, o <c>null</c> se non sono configurate o non si leggono (chiave dei segreti cambiata).</summary>
    Task<CredenzialiPayPal?> OttieniAsync(Guid idTenant, Guid idSito, CancellationToken annullamento);
}

public sealed record RichiestaOrdineProvider(
    Guid IdRichiesta,
    decimal Importo,
    string Valuta,
    string Riferimento,
    string Descrizione,
    Uri UrlRitorno,
    Uri UrlAnnullo);

public sealed record OrdineProvider(string IdProvider, Uri UrlApprovazione);

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

public sealed record EsitoRimborsoProvider(StatoRimborsoProvider Stato, string? IdRimborso);

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

using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

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

public sealed record SchedaNexiSito(string NomeSito, AmbientePagamento Ambiente, bool ChiaveImpostata, string? TokenConcorrenza, bool Configurato);

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

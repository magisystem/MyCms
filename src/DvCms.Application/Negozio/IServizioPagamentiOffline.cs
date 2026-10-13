using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;

namespace DvCms.Application.Negozio;

/// <summary>
/// Impostazioni dei pagamenti manuali di un sito (ADR-0023): coordinate del bonifico e condizioni del contrassegno. Non sono segrete:
/// l'IBAN si comunica ai clienti. Valori predefiniti: tutto spento.
/// </summary>
public sealed record ImpostazioniPagamentoOffline(
    bool BonificoAttivo,
    string? Intestatario,
    string? Iban,
    string? Bic,
    string? NomeBanca,
    int GiorniPerPagare,
    string? IstruzioniBonifico,
    bool ContrassegnoAttivo,
    decimal CommissioneFissa,
    decimal CommissionePercentuale,
    decimal? CommissioneGratisSopra,
    decimal? OrdineMinimo,
    decimal? OrdineMassimo,
    string? IstruzioniContrassegno)
{
    public static ImpostazioniPagamentoOffline Spente { get; } = new(
        false, null, null, null, null, ImpostazioniPagamentoOfflineSito.GiorniPerPagarePredefiniti, null, false, 0m, 0m, null, null, null, null);

    public CondizioniContrassegno Contrassegno => new(ContrassegnoAttivo, CommissioneFissa, CommissionePercentuale, CommissioneGratisSopra, OrdineMinimo, OrdineMassimo);
}

public sealed record SchedaPagamentoOffline(ImpostazioniPagamentoOffline Impostazioni, string? TokenConcorrenza);

/// <summary>Esito del preventivo di un pagamento per un metodo di consegna: se si può scegliere, la commissione e il totale da pagare.</summary>
public sealed record PreventivoPagamento(bool Disponibile, decimal Commissione, decimal Totale)
{
    /// <summary>
    /// Il pagamento <paramref name="modalita"/> per un ordine di <paramref name="totaleSenzaCommissione"/> (articoli e spedizione) consegnato con
    /// il metodo indicato. Il pagamento online non ha condizioni qui (i provider si valutano sulla pagina dell'ordine); il bonifico richiede
    /// solo di essere attivo; il contrassegno richiede la consegna in Italia, di essere attivo e il totale nei limiti.
    /// </summary>
    public static PreventivoPagamento Calcola(
        ImpostazioniPagamentoOffline impostazioni,
        ModalitaPagamento modalita,
        TipoMetodoSpedizione tipoSpedizione,
        AreaSpedizione? areaSpedizione,
        decimal totaleSenzaCommissione)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);
        switch (modalita)
        {
            case ModalitaPagamento.Online:
                return new PreventivoPagamento(true, 0m, totaleSenzaCommissione);
            case ModalitaPagamento.Bonifico:
                return new PreventivoPagamento(impostazioni.BonificoAttivo, 0m, totaleSenzaCommissione);
            case ModalitaPagamento.Contrassegno:
                var condizioni = impostazioni.Contrassegno;
                if (!RegoleContrassegno.MetodoAmmesso(tipoSpedizione, areaSpedizione) || !condizioni.Disponibile(totaleSenzaCommissione))
                {
                    return new PreventivoPagamento(false, 0m, totaleSenzaCommissione);
                }

                var commissione = condizioni.Commissione(totaleSenzaCommissione);
                return new PreventivoPagamento(true, commissione, totaleSenzaCommissione + commissione);
            default:
                return new PreventivoPagamento(false, 0m, totaleSenzaCommissione);
        }
    }
}

/// <summary>
/// Pagamenti manuali del sito (ADR-0023). Li configurano il <c>TenantAdmin</c> del tenant (o il <c>PlatformAdmin</c> che vi entra), con il
/// ruolo riverificato a ogni operazione e solo con il negozio attivo; chi può ordinare legge soltanto quelli attivi.
/// </summary>
public interface IServizioPagamentiOffline
{
    /// <summary>Amministrazione: le impostazioni complete (tutto spento se mai salvate).</summary>
    Task<SchedaPagamentoOffline> LeggiAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>
    /// Salva le impostazioni. <c>DatiNonValidi</c> (con i campi: IBAN non valido o mancante con il bonifico attivo, commissione fuori limiti,
    /// minimo oltre il massimo…), <c>NonConsentito</c> (negozio non attivo), <c>Conflitto</c> (modificate da un altro nel frattempo).
    /// </summary>
    Task<EsitoOperazione> SalvaAsync(Guid idAttore, ImpostazioniPagamentoOffline dati, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>Cassa e pagina dell'ordine: le impostazioni di ciò che è attivo (un metodo spento non mostra nulla di sé), per un account che può ordinare.</summary>
    Task<ImpostazioniPagamentoOffline> LeggiPerClienteAsync(Guid idAccount, CancellationToken annullamento);
}

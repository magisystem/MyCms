using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

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

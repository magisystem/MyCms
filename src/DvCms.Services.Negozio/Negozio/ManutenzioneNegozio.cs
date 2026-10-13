using DvCms.Contracts.Manutenzione;
using DvCms.Contracts.Negozio;
using DvCms.Services.Negozio.Pagamenti;

namespace DvCms.Services.Negozio;

/// <summary>Manutenzione periodica del negozio: giacenze scadute, fine conservazione dei dati personali e riconciliazione degli incassi.</summary>
public sealed class ManutenzioneNegozio : IAttivitaManutenzione
{
    private readonly IServizioGiacenze _giacenze;
    private readonly IServizioDatiPersonali _datiPersonali;
    private readonly RiconciliazionePagamenti _riconciliazione;

    public ManutenzioneNegozio(IServizioGiacenze giacenze, IServizioDatiPersonali datiPersonali, RiconciliazionePagamenti riconciliazione)
    {
        _giacenze = giacenze;
        _datiPersonali = datiPersonali;
        _riconciliazione = riconciliazione;
    }

    public async Task<int> EseguiAsync(CancellationToken annullamento)
    {
        var totale = 0;

        // Prenotazioni di giacenza scadute: liberano la merce e annullano gli ordini che non sono stati pagati.
        totale += await _giacenze.RilasciaScaduteAsync(annullamento);

        // Fine conservazione degli ordini (A7): si cancella la copia dei dati personali, restano importi e righe.
        totale += await _datiPersonali.AnonimizzaOrdiniScadutiAsync(annullamento);

        // Incassi mai notificati (ritorno del cliente perso, webhook non arrivato): si rilegge lo stato presso il provider. Non incassa nulla.
        totale += await _riconciliazione.EseguiAsync(annullamento);

        return totale;
    }
}

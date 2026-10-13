using DvCms.Application.Localizzazione;
using DvCms.Domain.Negozio;

namespace DvCms.Web;

/// <summary>Testi localizzati degli stati dell'ordine, uguali per area cliente e amministrazione.</summary>
public static class EtichetteOrdine
{
    public static string Stato(ILocalizzatore localizzatore, StatoOrdine stato)
    {
        ArgumentNullException.ThrowIfNull(localizzatore);
        return stato switch
        {
            StatoOrdine.InAttesaDiPagamento => localizzatore.Traduci("In attesa di pagamento"),
            StatoOrdine.PagamentoFallito => localizzatore.Traduci("Pagamento non riuscito"),
            StatoOrdine.Pagato => localizzatore.Traduci("Pagato"),
            StatoOrdine.Spedito => localizzatore.Traduci("Spedito"),
            StatoOrdine.Ritirato => localizzatore.Traduci("Ritirato"),
            StatoOrdine.Annullato => localizzatore.Traduci("Annullato"),
            StatoOrdine.Rimborsato => localizzatore.Traduci("Rimborsato"),
            StatoOrdine.DaSpedireContrassegno => localizzatore.Traduci("Da spedire (pagamento alla consegna)"),
            _ => stato.ToString(),
        };
    }

    public static string Modalita(ILocalizzatore localizzatore, DvCms.Domain.Negozio.ModalitaPagamento modalita)
    {
        ArgumentNullException.ThrowIfNull(localizzatore);
        return modalita switch
        {
            DvCms.Domain.Negozio.ModalitaPagamento.Online => localizzatore.Traduci("Pagamento online"),
            DvCms.Domain.Negozio.ModalitaPagamento.Bonifico => localizzatore.Traduci("Bonifico bancario"),
            DvCms.Domain.Negozio.ModalitaPagamento.Contrassegno => localizzatore.Traduci("Contrassegno"),
            _ => modalita.ToString(),
        };
    }
}

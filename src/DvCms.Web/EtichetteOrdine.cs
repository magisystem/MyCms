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
            _ => stato.ToString(),
        };
    }
}

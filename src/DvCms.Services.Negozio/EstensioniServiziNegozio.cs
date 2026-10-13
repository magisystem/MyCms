using DvCms.Contracts.Accessi;
using DvCms.Contracts.Accessi.DueFattori;
using DvCms.Contracts.Builder;
using DvCms.Contracts.Cms;
using DvCms.Contracts.Email;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Manutenzione;
using DvCms.Contracts.Media;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Outbox;
using DvCms.Contracts.Persistenza;
using DvCms.Contracts.Piattaforma;
using DvCms.Contracts.Temi;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Builder;
using DvCms.Core.Cache;
using DvCms.Core.Cms;
using DvCms.Core.Configurazione;
using DvCms.Core.Localizzazione;
using DvCms.Core.Media;
using DvCms.Core.Segreti;
using DvCms.Core.Temi;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.Data.Cache;
using DvCms.Data.Outbox;
using DvCms.Data.Popolamento;
using DvCms.Data.Segreti;
using DvCms.Data.Tenancy;
using DvCms.DTO.Accessi;
using DvCms.DTO.Accessi.DueFattori;
using DvCms.DTO.Cache;
using DvCms.DTO.Manutenzione;
using DvCms.DTO.Media;
using DvCms.DTO.Negozio;
using DvCms.DTO.Outbox;
using DvCms.Services.Negozio.Pagamenti;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DvCms.Services.Negozio;

public static class EstensioniServiziNegozio
{
    /// <summary>Registra catalogo, carrello, ordini, giacenze, spedizioni e i pagamenti (PayPal, Nexi, bonifico, contrassegno). Presuppone <c>AggiungiData</c> (ADR-0024).</summary>
    public static IServiceCollection AggiungiServiziNegozio(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        servizi.TryAddScoped<IServizioCatalogo, ServizioCatalogo>();
        servizi.TryAddScoped<IServizioSpedizioni, ServizioSpedizioni>();
        servizi.TryAddScoped<IServizioPagamentiOffline, ServizioPagamentiOffline>();
        servizi.TryAddScoped<IServizioCarrello, ServizioCarrello>();
        servizi.TryAddScoped<IServizioVetrina, ServizioVetrina>();
        servizi.TryAddScoped<IServizioCategorie, ServizioCategorie>();
        servizi.TryAddScoped<IServizioOrdini, ServizioOrdini>();
        servizi.TryAddScoped<IServizioEsportazioneOrdini, ServizioEsportazioneOrdini>();
        servizi.TryAddScoped<IServizioGiacenze, ServizioGiacenze>();
        servizi.AddHttpClient(ClientePayPal.NomeClientHttp, client => client.Timeout = Timeout.InfiniteTimeSpan);
        servizi.TryAddScoped<IProviderPagamento, ClientePayPal>();
        servizi.AddHttpClient(ClienteNexi.NomeClientHttp, client => client.Timeout = Timeout.InfiniteTimeSpan);
        servizi.TryAddScoped<ClienteNexi>();
        servizi.TryAddScoped<ServizioImpostazioniNexi>();
        servizi.TryAddScoped<IServizioImpostazioniNexi>(f => f.GetRequiredService<ServizioImpostazioniNexi>());
        servizi.AddScoped<IGatewayPagamento, GatewayPayPal>();
        servizi.AddScoped<IGatewayPagamento, GatewayNexi>();
        servizi.TryAddSingleton(new OpzioniNegozio(configurazione.GetValue<bool>("Negozio:CheckoutProduzione"), configurazione.GetValue("Negozio:AvvisoNuovoOrdineAmministratori", true)));
        servizi.TryAddScoped<NotificheOrdine>();
        servizi.TryAddScoped<IServizioRimborsi, ServizioRimborsi>();
        servizi.TryAddSingleton(new OpzioniDatiPersonali(configurazione.LeggiIntero("Negozio:ConservazioneOrdiniAnni", OpzioniDatiPersonali.Predefinite.ConservazioneOrdiniAnni, 1, 30)));
        servizi.TryAddScoped<IServizioDatiPersonali, ServizioDatiPersonali>();
        servizi.TryAddScoped<IServizioPagamenti, ServizioPagamenti>();
        servizi.TryAddScoped<RiconciliazionePagamenti>();
        servizi.AddScoped<IAttivitaManutenzione, ManutenzioneNegozio>();
        servizi.TryAddScoped<IServizioProntezzaNegozio, ServizioProntezzaNegozio>();
        servizi.TryAddScoped<ServizioImpostazioniPagamento>();
        servizi.TryAddScoped<IServizioImpostazioniPagamento>(f => f.GetRequiredService<ServizioImpostazioniPagamento>());
        servizi.TryAddScoped<IArchivioCredenzialiPagamento>(f => f.GetRequiredService<ServizioImpostazioniPagamento>());

        return servizi;
    }
}

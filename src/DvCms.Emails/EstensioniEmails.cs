using DvCms.Contracts.Email;
using DvCms.Contracts.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DvCms.Emails;

public static class EstensioniEmails
{
    /// <summary>Registra gli adapter dei canali (SMTP, Microsoft Graph, Gmail, SendGrid, OAuth), le impostazioni per tenant e l'invio dall'outbox.</summary>
    public static IServiceCollection AggiungiEmails(this IServiceCollection servizi)
    {
        ArgumentNullException.ThrowIfNull(servizi);

        servizi.AddScoped<IAdapterEmail, AdapterSmtp>();
        servizi.AddHttpClient(ComunicazioneEmail.NomeClientHttp, client => client.Timeout = Timeout.InfiniteTimeSpan);
        servizi.AddScoped<IAdapterEmail, AdapterGraph>();
        servizi.AddScoped<IAdapterEmail, AdapterGmail>();
        servizi.AddScoped<IAdapterEmail, AdapterSendGrid>();
        servizi.AddScoped<IAdapterEmail, AdapterMicrosoftOAuth>();
        servizi.AddScoped<IAdapterEmail, AdapterGoogleOAuth>();
        servizi.AddScoped<ClienteOAuthEmail>();
        servizi.AddScoped<GestoreAccessoEmailOAuth>();
        servizi.TryAddScoped<IServizioConnessioneEmail, ServizioConnessioneEmail>();
        servizi.TryAddScoped<IInvioEmail, InvioEmailPerTenant>();
        servizi.TryAddScoped<IAccodatoreEmail, AccodatoreEmail>();
        servizi.TryAddScoped<IServizioImpostazioniEmail, ServizioImpostazioniEmail>();
        servizi.AddScoped<IGestoreOutbox, GestoreInvioEmail>();

        return servizi;
    }
}

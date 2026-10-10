using System.Globalization;
using DvCms.Application.Email;
using DvCms.Domain.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DvCms.Infrastructure.Email;

/// <summary>Canale SMTP (MailKit). Primo adapter, comodo anche con un catcher locale in sviluppo.</summary>
public sealed class AdapterSmtp : IAdapterEmail
{
    private static readonly TimeSpan _attesaMassima = TimeSpan.FromSeconds(30);

    public ProviderEmail Provider => ProviderEmail.Smtp;

    public async Task InviaAsync(ImpostazioniEmail impostazioni, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);
        ArgumentNullException.ThrowIfNull(messaggio);

        var mime = CostruisciMessaggio(impostazioni, messaggio);
        var host = impostazioni.Valore("Host")!;
        var porta = int.Parse(impostazioni.Valore("Porta")!, NumberStyles.None, CultureInfo.InvariantCulture);
        var sicurezza = (impostazioni.Valore("ModalitaTls") ?? DefinizioniProviderEmail.TlsStartTls) switch
        {
            DefinizioniProviderEmail.TlsNessuna => SecureSocketOptions.None,
            DefinizioniProviderEmail.TlsImmediato => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls,
        };

        using var client = new SmtpClient { Timeout = (int)_attesaMassima.TotalMilliseconds };
        await client.ConnectAsync(host, porta, sicurezza, annullamento);
        if (impostazioni.Valore("Utente") is { } utente)
        {
            await client.AuthenticateAsync(utente, impostazioni.Valore("Password") ?? string.Empty, annullamento);
        }

        await client.SendAsync(mime, annullamento);
        await client.DisconnectAsync(true, annullamento);
    }

    /// <summary>Costruisce il messaggio MIME. Rifiuta intestazioni con a capo (header injection).</summary>
    internal static MimeMessage CostruisciMessaggio(ImpostazioniEmail impostazioni, MessaggioEmail messaggio) =>
        ComunicazioneEmail.CostruisciMime(impostazioni.Valore("IndirizzoMittente")!, impostazioni.Valore("NomeMittente"), messaggio);
}

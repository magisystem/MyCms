using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>Messaggio ricevuto dal server SMTP finto.</summary>
public sealed record EmailRicevuta(string Mittente, IReadOnlyList<string> Destinatari, string Dati, string? Credenziali)
{
    private MimeKit.MimeMessage Messaggio => MimeKit.MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(Dati)));

    /// <summary>Oggetto decodificato.</summary>
    public string Oggetto => Messaggio.Subject ?? string.Empty;

    /// <summary>Corpo in testo semplice, decodificato (le codifiche di trasferimento sono già tolte).</summary>
    public string Testo => Messaggio.TextBody ?? string.Empty;
}

/// <summary>
/// Server SMTP minimo in locale (nessuna email reale parte dai test): accetta connessioni in chiaro, anche con
/// AUTH PLAIN, e conserva ciò che riceve. Con <see cref="RifiutaInvio"/> risponde con un errore permanente.
/// </summary>
public sealed class ServerSmtpFinto : IAsyncDisposable
{
    private readonly TcpListener _ascolto;
    private readonly CancellationTokenSource _arresto = new();
    private readonly List<EmailRicevuta> _ricevute = [];
    private readonly Task _ciclo;

    public ServerSmtpFinto()
    {
        _ascolto = new TcpListener(IPAddress.Loopback, 0);
        _ascolto.Start();
        Porta = ((IPEndPoint)_ascolto.LocalEndpoint).Port;
        _ciclo = Task.Run(AccettaAsync);
    }

    public int Porta { get; }

    public bool RifiutaInvio { get; set; }

    public IReadOnlyList<EmailRicevuta> Ricevute
    {
        get
        {
            lock (_ricevute)
            {
                return [.. _ricevute];
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _arresto.CancelAsync();
        _ascolto.Stop();
        try
        {
            await _ciclo;
        }
        catch (OperationCanceledException)
        {
            // atteso allo spegnimento
        }

        _arresto.Dispose();
    }

    private async Task AccettaAsync()
    {
        while (!_arresto.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _ascolto.AcceptTcpClientAsync(_arresto.Token);
            }
            catch (Exception eccezione) when (eccezione is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => SessioneAsync(client));
        }
    }

    private async Task SessioneAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var flusso = client.GetStream();
                using var lettore = new StreamReader(flusso, Encoding.ASCII);
                await using var scrittore = new StreamWriter(flusso, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                await scrittore.WriteLineAsync("220 finto ESMTP");

                var mittente = string.Empty;
                var destinatari = new List<string>();
                string? credenziali = null;
                string? riga;
                while ((riga = await lettore.ReadLineAsync(_arresto.Token)) is not null)
                {
                    if (riga.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || riga.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                    {
                        await scrittore.WriteLineAsync("250-finto");
                        await scrittore.WriteLineAsync("250 AUTH PLAIN");
                    }
                    else if (riga.StartsWith("AUTH PLAIN", StringComparison.OrdinalIgnoreCase))
                    {
                        var dati = riga.Length > 11 ? riga[11..] : (await scrittore.WriteLineAsyncThenRead(lettore, "334 "));
                        credenziali = Encoding.UTF8.GetString(Convert.FromBase64String(dati)).Replace('\0', '|');
                        await scrittore.WriteLineAsync("235 autenticato");
                    }
                    else if (riga.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase))
                    {
                        mittente = Estrai(riga);
                        await scrittore.WriteLineAsync("250 ok");
                    }
                    else if (riga.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                    {
                        destinatari.Add(Estrai(riga));
                        await scrittore.WriteLineAsync("250 ok");
                    }
                    else if (riga.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        if (RifiutaInvio)
                        {
                            await scrittore.WriteLineAsync("554 rifiutato");
                            continue;
                        }

                        await scrittore.WriteLineAsync("354 invia");
                        var corpo = new StringBuilder();
                        while ((riga = await lettore.ReadLineAsync(_arresto.Token)) is not null && riga != ".")
                        {
                            // Trasparenza SMTP (RFC 5321, 4.5.2): una riga che inizia con ".." è una riga che inizia con ".".
                            corpo.AppendLine(riga.StartsWith("..", StringComparison.Ordinal) ? riga[1..] : riga);
                        }

                        lock (_ricevute)
                        {
                            _ricevute.Add(new EmailRicevuta(mittente, [.. destinatari], corpo.ToString(), credenziali));
                        }

                        destinatari.Clear();
                        await scrittore.WriteLineAsync("250 accodato");
                    }
                    else if (riga.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await scrittore.WriteLineAsync("221 ciao");
                        return;
                    }
                    else
                    {
                        await scrittore.WriteLineAsync("250 ok");
                    }
                }
            }
            catch (Exception eccezione) when (eccezione is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // il client ha chiuso o il test è finito
            }
        }
    }

    private static string Estrai(string riga)
    {
        var inizio = riga.IndexOf('<', StringComparison.Ordinal);
        var fine = riga.IndexOf('>', StringComparison.Ordinal);
        return inizio >= 0 && fine > inizio ? riga[(inizio + 1)..fine] : riga;
    }
}

internal static class EstensioniScrittore
{
    public static async Task<string> WriteLineAsyncThenRead(this StreamWriter scrittore, StreamReader lettore, string risposta)
    {
        await scrittore.WriteLineAsync(risposta);
        return await lettore.ReadLineAsync() ?? string.Empty;
    }
}

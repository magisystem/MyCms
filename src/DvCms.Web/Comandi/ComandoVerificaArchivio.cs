using DvCms.Infrastructure.Media;

namespace DvCms.Web.Comandi;

/// <summary>
/// Comando <c>DvCms.Web verifica-archivio</c>: dopo un ripristino confronta i file dell'archivio con i metadati del database
/// (oggetti mancanti, impronte diverse, oggetti senza riga) e termina. Codice di uscita 0 se tutto è coerente, 1 altrimenti.
/// Stampa solo le chiavi degli oggetti (che non contengono dati degli utenti), mai i contenuti.
/// </summary>
public static partial class ComandoVerificaArchivio
{
    public const string Nome = "verifica-archivio";

    public static async Task<int> EseguiAsync(IServiceProvider servizi, CancellationToken annullamento = default)
    {
        ArgumentNullException.ThrowIfNull(servizi);

        var logger = servizi.GetRequiredService<ILoggerFactory>().CreateLogger("DvCms.VerificaArchivio");
        try
        {
            await using var ambito = servizi.CreateAsyncScope();
            var esito = await ambito.ServiceProvider.GetRequiredService<VerificatoreArchivio>().VerificaAsync(annullamento);

            RegistraRiepilogo(logger, esito.RisorseControllate, esito.OggettiMancanti.Count, esito.ImprontaDiversa.Count, esito.OggettiOrfani.Count);
            foreach (var chiave in esito.OggettiMancanti)
            {
                RegistraMancante(logger, chiave);
            }

            foreach (var chiave in esito.ImprontaDiversa)
            {
                RegistraDiversa(logger, chiave);
            }

            foreach (var chiave in esito.OggettiOrfani)
            {
                RegistraOrfano(logger, chiave);
            }

            return esito.Coerente ? 0 : 1;
        }
        catch (Exception eccezione)
        {
            RegistraErrore(logger, eccezione);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Archivio: {Risorse} risorse controllate, {Mancanti} oggetti mancanti, {Diversi} con impronta diversa, {Orfani} oggetti senza riga")]
    private static partial void RegistraRiepilogo(ILogger logger, int risorse, int mancanti, int diversi, int orfani);

    [LoggerMessage(Level = LogLevel.Error, Message = "Archivio: oggetto mancante {Chiave}")]
    private static partial void RegistraMancante(ILogger logger, string chiave);

    [LoggerMessage(Level = LogLevel.Error, Message = "Archivio: impronta diversa da quella registrata per {Chiave}")]
    private static partial void RegistraDiversa(ILogger logger, string chiave);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Archivio: oggetto senza riga nel database {Chiave}")]
    private static partial void RegistraOrfano(ILogger logger, string chiave);

    [LoggerMessage(Level = LogLevel.Error, Message = "Verifica dell'archivio non riuscita")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}

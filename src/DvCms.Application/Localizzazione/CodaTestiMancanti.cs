using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DvCms.Application.Localizzazione;

/// <summary>
/// Chiavi non ancora presenti nell'archivio, in attesa di essere scritte da un processo in background.
/// Ogni chiave è accodata una sola volta; il rendering delle viste non scrive mai sul database.
/// </summary>
public sealed class CodaTestiMancanti
{
    private readonly ConcurrentDictionary<string, byte> _inAttesa = new();
    private readonly Channel<string> _canale = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public int InAttesa => _inAttesa.Count;

    public bool Accoda(string chiave)
    {
        if (!_inAttesa.TryAdd(chiave, 0))
        {
            return false;
        }

        _canale.Writer.TryWrite(chiave);
        return true;
    }

    public bool TentaPrelevare(out string chiave)
    {
        return _canale.Reader.TryRead(out chiave!);
    }

    public void Completata(string chiave) => _inAttesa.TryRemove(chiave, out _);

    public async Task AttendiAsync(CancellationToken annullamento)
    {
        await _canale.Reader.WaitToReadAsync(annullamento);
    }
}

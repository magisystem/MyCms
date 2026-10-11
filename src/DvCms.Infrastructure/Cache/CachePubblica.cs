using System.Collections.Concurrent;
using DvCms.Application.Cms;

namespace DvCms.Infrastructure.Cache;

/// <summary>Parametri della cache (configurazione <c>Cache:*</c>).</summary>
public sealed record OpzioniCache(bool Abilitata, TimeSpan Durata, int VociMassime, TimeSpan IntervalloLettura)
{
    public static OpzioniCache Predefinite { get; } = new(true, TimeSpan.FromMinutes(5), 5000, TimeSpan.FromSeconds(2));
}

/// <summary>
/// Implementazione in memoria di <see cref="ICachePubblica"/>. Le voci sono immutabili e condivise fra le richieste. Se supera il
/// numero massimo di voci elimina prima quelle scadute o di generazioni vecchie, poi le più vecchie: la memoria è limitata anche
/// con molti siti o con chi cerca indirizzi a caso.
/// </summary>
public sealed class CachePubblica : ICachePubblica
{
    private readonly OpzioniCache _opzioni;
    private readonly TimeProvider _orologio;
    private readonly ConcurrentDictionary<string, Voce> _voci = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, long> _generazioni = new();
    private readonly object _sincronizzazione = new();

    public CachePubblica(OpzioniCache opzioni, TimeProvider orologio)
    {
        _opzioni = opzioni;
        _orologio = orologio;
    }

    public int Voci => _voci.Count;

    public async Task<T?> OttieniAsync<T>(Guid idSito, string chiave, Func<Task<T?>> carica)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(carica);
        if (!_opzioni.Abilitata)
        {
            MetricheCache.Accesso("disabilitata");
            return await carica();
        }

        // La generazione si legge prima del caricamento: se il sito viene invalidato mentre si carica, la voce resta legata alla
        // generazione vecchia e nessuno la rivedrà (meglio un ricaricamento in più che un contenuto superato).
        var generazione = _generazioni.GetValueOrDefault(idSito);
        var chiaveCompleta = $"{idSito:N}|{generazione}|{chiave}";
        var adesso = _orologio.GetUtcNow();
        if (_voci.TryGetValue(chiaveCompleta, out var voce) && voce.Scade > adesso && voce.Valore is T trovato)
        {
            MetricheCache.Accesso("trovato");
            return trovato;
        }

        MetricheCache.Accesso("mancato");

        var caricato = await carica();
        if (caricato is not null)
        {
            Memorizza(chiaveCompleta, caricato, adesso + _opzioni.Durata);
        }

        return caricato;
    }

    public void Invalida(Guid idSito)
    {
        _generazioni.AddOrUpdate(idSito, 1, (_, attuale) => attuale + 1);
        MetricheCache.Invalidazione();
    }

    private void Memorizza(string chiave, object valore, DateTimeOffset scade)
    {
        if (_voci.Count >= _opzioni.VociMassime)
        {
            Sfoltisci();
        }

        _voci[chiave] = new Voce(valore, scade);
    }

    private void Sfoltisci()
    {
        lock (_sincronizzazione)
        {
            if (_voci.Count < _opzioni.VociMassime)
            {
                return;
            }

            var adesso = _orologio.GetUtcNow();
            foreach (var coppia in _voci.Where(c => c.Value.Scade <= adesso || !GenerazioneAttuale(c.Key)).ToList())
            {
                _voci.TryRemove(coppia.Key, out _);
            }

            if (_voci.Count >= _opzioni.VociMassime)
            {
                // Ancora pieno: si libera un decimo, partendo dalle voci che scadono prima.
                foreach (var coppia in _voci.OrderBy(c => c.Value.Scade).Take(Math.Max(1, _opzioni.VociMassime / 10)).ToList())
                {
                    _voci.TryRemove(coppia.Key, out _);
                }
            }
        }
    }

    /// <summary>Vero se la voce appartiene alla generazione in corso del suo sito (la chiave è <c>sito|generazione|resto</c>).</summary>
    private bool GenerazioneAttuale(string chiaveCompleta)
    {
        var parti = chiaveCompleta.Split('|', 3);
        return parti.Length == 3
            && Guid.TryParseExact(parti[0], "N", out var idSito)
            && long.TryParse(parti[1], out var generazione)
            && generazione == _generazioni.GetValueOrDefault(idSito);
    }

    private sealed record Voce(object Valore, DateTimeOffset Scade);
}

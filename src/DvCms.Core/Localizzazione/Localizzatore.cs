using System.Globalization;
using DvCms.Contracts.Localizzazione;
using DvCms.Models.Localizzazione;

namespace DvCms.Core.Localizzazione;

/// <summary>
/// Algoritmo di ADR-0018: se il testo esiste per la lingua si usa quello; altrimenti si restituisce il testo italiano
/// (che è la chiave stessa, quindi sempre disponibile) e la chiave viene accodata per essere scritta nell'archivio
/// con il testo italiano e <c>DaTradurre</c> vero.
/// </summary>
public sealed class Localizzatore : ILocalizzatore
{
    private readonly CacheTestiStatici _cache;
    private readonly CodaTestiMancanti _coda;
    private readonly IContestoLingua _contestoLingua;

    public Localizzatore(CacheTestiStatici cache, CodaTestiMancanti coda, IContestoLingua contestoLingua)
    {
        _cache = cache;
        _coda = coda;
        _contestoLingua = contestoLingua;
    }

    public string this[string chiave, params object?[] argomenti] => Traduci(chiave, argomenti);

    public string Traduci(string chiave, params object?[] argomenti) =>
        TraduciIn(_contestoLingua.CodiceLingua, chiave, argomenti);

    public string TraduciIn(string codiceLingua, string chiave, params object?[] argomenti)
    {
        ArgumentNullException.ThrowIfNull(codiceLingua);
        ArgumentNullException.ThrowIfNull(chiave);

        var cultura = _cache.CulturaDi(codiceLingua);

        if (_cache.TentaLeggi(codiceLingua, chiave, out var testo))
        {
            return FormattatoreTesti.Formatta(testo, cultura, argomenti);
        }

        // Testo mancante: si accoda per la scrittura (una sola volta) e si usa l'italiano di base.
        if (chiave.Length <= TestoStatico.LunghezzaMassimaChiave)
        {
            _coda.Accoda(chiave);
        }

        if (!string.Equals(codiceLingua, Lingua.CodiceItaliano, StringComparison.OrdinalIgnoreCase)
            && _cache.TentaLeggi(Lingua.CodiceItaliano, chiave, out var italiano))
        {
            return FormattatoreTesti.Formatta(italiano, cultura, argomenti);
        }

        return FormattatoreTesti.Formatta(chiave, cultura, argomenti);
    }
}

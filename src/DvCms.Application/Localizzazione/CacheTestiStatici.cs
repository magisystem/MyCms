using System.Collections.Concurrent;
using System.Globalization;

namespace DvCms.Application.Localizzazione;

/// <summary>
/// Cache in memoria dei testi statici per lingua. La lettura non interroga mai il database.
/// L'invalidazione è locale all'istanza (decisione per ora, ADR-0018).
/// </summary>
public sealed class CacheTestiStatici
{
    private readonly ConcurrentDictionary<(string Lingua, string Chiave), string> _testi = new();
    private readonly ConcurrentDictionary<string, CultureInfo> _culture = new(StringComparer.OrdinalIgnoreCase);

    public bool TentaLeggi(string codiceLingua, string chiave, out string testo)
    {
        if (_testi.TryGetValue((NormalizzaCodice(codiceLingua), chiave), out var trovato))
        {
            testo = trovato;
            return true;
        }

        testo = string.Empty;
        return false;
    }

    public void Imposta(string codiceLingua, string chiave, string testo)
    {
        _testi[(NormalizzaCodice(codiceLingua), chiave)] = testo;
    }

    public void Carica(IEnumerable<RigaTestoStatico> righe)
    {
        ArgumentNullException.ThrowIfNull(righe);

        foreach (var riga in righe)
        {
            Imposta(riga.CodiceLingua, riga.Chiave, riga.Testo);
        }
    }

    public void ImpostaLingue(IEnumerable<InfoLingua> lingue)
    {
        ArgumentNullException.ThrowIfNull(lingue);

        foreach (var lingua in lingue)
        {
            try
            {
                _culture[NormalizzaCodice(lingua.Codice)] = CultureInfo.GetCultureInfo(lingua.Cultura);
            }
            catch (CultureNotFoundException)
            {
                _culture[NormalizzaCodice(lingua.Codice)] = CultureInfo.InvariantCulture;
            }
        }
    }

    public CultureInfo CulturaDi(string codiceLingua) =>
        _culture.TryGetValue(NormalizzaCodice(codiceLingua), out var cultura) ? cultura : CultureInfo.InvariantCulture;

    public int Conteggio => _testi.Count;

    private static string NormalizzaCodice(string codice) => codice.ToLowerInvariant();
}

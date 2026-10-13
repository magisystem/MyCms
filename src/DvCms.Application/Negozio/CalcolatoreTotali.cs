using DvCms.Models.Negozio;

namespace DvCms.Application.Negozio;

/// <summary>
/// Una riga calcolata. Il prezzo è **IVA inclusa** (ADR-0020): <see cref="Imponibile"/> e <see cref="Iva"/> sono lo scorporo del
/// <see cref="Lordo"/> e la loro somma è sempre uguale al lordo, al centesimo.
/// </summary>
public sealed record RigaCalcolata(decimal PrezzoUnitarioLordo, int Quantita, decimal Aliquota, decimal Lordo, decimal Imponibile, decimal Iva);

public sealed record TotaliCalcolati(IReadOnlyList<RigaCalcolata> Righe, decimal Lordo, decimal Imponibile, decimal Iva);

/// <summary>
/// Calcolo dei totali (F-002), puro e senza accesso ai dati: dato prezzo, aliquota e quantità restituisce sempre lo stesso
/// risultato. Regola (ADR-0020): il lordo di riga è prezzo × quantità (esatto); l'imponibile di riga è il lordo diviso
/// (1 + aliquota/100), arrotondato al centesimo (lontano dallo zero); l'IVA di riga è la differenza, così lordo = imponibile + IVA
/// per ogni riga e per i totali, che sono somme delle righe (mai ricalcolate sul totale).
/// </summary>
public static class CalcolatoreTotali
{
    public static RigaCalcolata CalcolaRiga(decimal prezzoUnitarioLordo, int quantita, decimal aliquota)
    {
        if (!VarianteProdotto.PrezzoValido(prezzoUnitarioLordo))
        {
            throw new ArgumentOutOfRangeException(nameof(prezzoUnitarioLordo), "Prezzo non valido.");
        }

        if (quantita < 1 || quantita > RigaCarrello.QuantitaMassima)
        {
            throw new ArgumentOutOfRangeException(nameof(quantita), "Quantità non valida.");
        }

        if (!AliquoteIva.EAmmessa(aliquota))
        {
            throw new ArgumentOutOfRangeException(nameof(aliquota), "Aliquota IVA non ammessa.");
        }

        var lordo = prezzoUnitarioLordo * quantita;
        var imponibile = decimal.Round(lordo * 100m / (100m + aliquota), 2, MidpointRounding.AwayFromZero);
        return new RigaCalcolata(prezzoUnitarioLordo, quantita, aliquota, lordo, imponibile, lordo - imponibile);
    }

    /// <summary>
    /// Spedizione: la tariffa è IVA inclusa con l'aliquota ordinaria (ADR-0020) e si scorpora come una riga. Una tariffa zero
    /// (ritiro in sede o spedizione gratuita) dà una riga a zero.
    /// </summary>
    public static RigaCalcolata CalcolaSpedizione(decimal tariffaLorda)
    {
        if (!MetodoSpedizione.TariffaValida(tariffaLorda))
        {
            throw new ArgumentOutOfRangeException(nameof(tariffaLorda), "Tariffa non valida.");
        }

        return tariffaLorda == 0m
            ? new RigaCalcolata(0m, 1, AliquoteIva.Predefinita, 0m, 0m, 0m)
            : CalcolaRiga(tariffaLorda, 1, AliquoteIva.Predefinita);
    }

    /// <summary>Commissione del contrassegno (ADR-0023): importo IVA inclusa con l'aliquota ordinaria, scorporato come la spedizione; zero dà una riga a zero.</summary>
    public static RigaCalcolata CalcolaCommissione(decimal commissioneLorda)
    {
        if (!ImpostazioniPagamentoOfflineSito.ImportoValido(commissioneLorda))
        {
            throw new ArgumentOutOfRangeException(nameof(commissioneLorda), "Commissione non valida.");
        }

        return commissioneLorda == 0m
            ? new RigaCalcolata(0m, 1, AliquoteIva.Predefinita, 0m, 0m, 0m)
            : CalcolaRiga(commissioneLorda, 1, AliquoteIva.Predefinita);
    }

    public static TotaliCalcolati Somma(IEnumerable<RigaCalcolata> righe)
    {
        ArgumentNullException.ThrowIfNull(righe);

        var elenco = righe.ToList();
        return new TotaliCalcolati(elenco, elenco.Sum(r => r.Lordo), elenco.Sum(r => r.Imponibile), elenco.Sum(r => r.Iva));
    }
}

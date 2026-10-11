using System.Text.RegularExpressions;

namespace DvCms.Domain.Negozio;

public enum StatoProdotto
{
    /// <summary>Si vede solo nell'amministrazione.</summary>
    Bozza = 1,

    /// <summary>Visibile ai clienti (con almeno una variante attiva).</summary>
    Pubblicato = 2,

    /// <summary>Non più in vendita ma conservato (gli ordini passati lo referenziano).</summary>
    Archiviato = 3,
}

/// <summary>Aliquote IVA ammesse (ADR-0020): per ora le aliquote italiane; 22% è quella ordinaria e predefinita.</summary>
public static class AliquoteIva
{
    public const decimal Predefinita = 22m;

    public static IReadOnlyList<decimal> Ammesse { get; } = [0m, 4m, 5m, 10m, 22m];

    public static bool EAmmessa(decimal aliquota) => Ammesse.Contains(aliquota);
}

/// <summary>
/// Un prodotto del negozio di un sito (F-001). Nome e descrizione stanno nelle traduzioni (F-006); prezzi e codici nelle varianti
/// (anche un prodotto "semplice" ha almeno una variante). Il prodotto non conosce giacenza né ordini: li hanno i moduli dedicati.
/// </summary>
public class Prodotto
{
    public const int LunghezzaMassimaNome = 200;
    public const int LunghezzaMassimaDescrizione = 5000;
    public const int NumeroMassimoVarianti = 100;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public StatoProdotto Stato { get; set; } = StatoProdotto.Bozza;

    /// <summary>Aliquota IVA in percentuale (i prezzi delle varianti sono IVA inclusa).</summary>
    public decimal AliquotaIva { get; set; } = AliquoteIva.Predefinita;

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

/// <summary>
/// Variante vendibile di un prodotto: ha il suo codice (SKU, unico nel sito) e il suo prezzo **IVA inclusa**. L'importo è un
/// <see cref="decimal"/> con due decimali e la valuta è esplicita (per ora solo EUR, ADR-0020).
/// </summary>
public partial class VarianteProdotto
{
    public const string ValutaPredefinita = "EUR";
    public const decimal PrezzoMassimo = 1_000_000m;
    public const int LunghezzaMassimaEtichetta = 100;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdProdotto { get; set; }

    /// <summary>Codice articolo, maiuscolo (vedi <see cref="TentaNormalizzareSku"/>).</summary>
    public string Sku { get; set; } = string.Empty;

    public decimal PrezzoLordo { get; set; }

    public string Valuta { get; set; } = ValutaPredefinita;

    public bool Attiva { get; set; } = true;

    public int Posizione { get; set; }

    /// <summary>Normalizza un codice articolo (maiuscolo; lettere, cifre, punto, trattino, trattino basso; fino a 50 caratteri).</summary>
    public static bool TentaNormalizzareSku(string? valore, out string sku)
    {
        sku = (valore ?? string.Empty).Trim().ToUpperInvariant();
        return FormatoSku().IsMatch(sku);
    }

    /// <summary>Importo valido: positivo, non oltre il massimo e con al più due decimali (nessun arrotondamento silenzioso).</summary>
    public static bool PrezzoValido(decimal prezzo) =>
        prezzo > 0 && prezzo <= PrezzoMassimo && decimal.Round(prezzo, 2) == prezzo;

    public static bool ValutaAmmessa(string? valuta) => string.Equals(valuta, ValutaPredefinita, StringComparison.Ordinal);

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{0,49}$")]
    private static partial Regex FormatoSku();
}

public class TraduzioneProdotto
{
    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdProdotto { get; set; }

    public Guid IdLingua { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string Descrizione { get; set; } = string.Empty;
}

public class TraduzioneVarianteProdotto
{
    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdVariante { get; set; }

    public Guid IdLingua { get; set; }

    public string Etichetta { get; set; } = string.Empty;
}

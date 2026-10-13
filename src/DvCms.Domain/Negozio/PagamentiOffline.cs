using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace DvCms.Domain.Negozio;

/// <summary>
/// Come il cliente paga un ordine (ADR-0023). <see cref="Online"/> è il pagamento con un provider (PayPal, Nexi) scelto sulla pagina
/// dell'ordine; <see cref="Bonifico"/> e <see cref="Contrassegno"/> sono pagamenti manuali, scelti alla cassa perché cambiano
/// l'ordine (scadenza della prenotazione, commissione) e confermati dal tenant. I valori sono salvati come testo: non rinominarli.
/// </summary>
public enum ModalitaPagamento
{
    Online = 1,
    Bonifico = 2,
    Contrassegno = 3,
}

/// <summary>
/// Pagamenti manuali di un sito (ADR-0023): coordinate del bonifico e condizioni del contrassegno, gestite dal <c>TenantAdmin</c>.
/// Nessun dato è segreto: l'IBAN si comunica al cliente per poter pagare. Una riga per sito, creata al primo salvataggio.
/// </summary>
public class ImpostazioniPagamentoOfflineSito
{
    public const int LunghezzaMassimaIntestatario = 150;
    public const int LunghezzaMassimaBanca = 100;
    public const int LunghezzaMassimaIstruzioni = 500;
    public const int GiorniPerPagareMinimi = 1;
    public const int GiorniPerPagareMassimi = 60;
    public const int GiorniPerPagarePredefiniti = 7;
    public const decimal ImportoMassimo = 100_000m;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    // Bonifico bancario.
    public bool BonificoAttivo { get; set; }

    public string? Intestatario { get; set; }

    /// <summary>IBAN normalizzato (maiuscolo, senza spazi).</summary>
    public string? Iban { get; set; }

    public string? Bic { get; set; }

    public string? NomeBanca { get; set; }

    /// <summary>Entro quanti giorni dall'ordine il bonifico deve arrivare: passati, la merce prenotata torna libera e l'ordine si annulla.</summary>
    public int GiorniPerPagare { get; set; } = GiorniPerPagarePredefiniti;

    public string? IstruzioniBonifico { get; set; }

    // Contrassegno (pagamento alla consegna).
    public bool ContrassegnoAttivo { get; set; }

    /// <summary>Importo fisso della commissione, IVA inclusa (ordinaria).</summary>
    public decimal CommissioneFissa { get; set; }

    /// <summary>Percentuale sul totale dell'ordine (articoli e spedizione, IVA inclusa), da 0 a 100 con due decimali; si somma alla parte fissa.</summary>
    public decimal CommissionePercentuale { get; set; }

    /// <summary>Dal totale d'ordine in su la commissione non si applica; nullo = mai gratis.</summary>
    public decimal? CommissioneGratisSopra { get; set; }

    /// <summary>Totale minimo dell'ordine per il contrassegno; nullo = nessun minimo.</summary>
    public decimal? OrdineMinimo { get; set; }

    /// <summary>Totale massimo dell'ordine per il contrassegno (limite del corriere o del negozio); nullo = nessun massimo.</summary>
    public decimal? OrdineMassimo { get; set; }

    public string? IstruzioniContrassegno { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    public static bool ImportoValido(decimal importo) => importo >= 0 && importo <= ImportoMassimo && decimal.Round(importo, 2) == importo;

    public static bool PercentualeValida(decimal percentuale) => percentuale >= 0 && percentuale <= 100 && decimal.Round(percentuale, 2) == percentuale;

    public CondizioniContrassegno Condizioni() => new(ContrassegnoAttivo, CommissioneFissa, CommissionePercentuale, CommissioneGratisSopra, OrdineMinimo, OrdineMassimo);
}

/// <summary>Condizioni del contrassegno di un sito (ADR-0023): quando è offerto e quanto costa.</summary>
/// <param name="Attivo">Il tenant offre il contrassegno.</param>
/// <param name="CommissioneFissa">Parte fissa, IVA inclusa.</param>
/// <param name="CommissionePercentuale">Percentuale sul totale dell'ordine prima della commissione (da 0 a 100).</param>
/// <param name="CommissioneGratisSopra">Dal totale d'ordine in su la commissione non si applica; nullo = mai gratis.</param>
/// <param name="OrdineMinimo">Totale minimo per offrirlo; nullo = nessun minimo.</param>
/// <param name="OrdineMassimo">Totale massimo per offrirlo (limite del corriere o del negozio); nullo = nessun massimo.</param>
public sealed record CondizioniContrassegno(bool Attivo, decimal CommissioneFissa, decimal CommissionePercentuale, decimal? CommissioneGratisSopra, decimal? OrdineMinimo, decimal? OrdineMassimo)
{
    /// <summary>Vero se il contrassegno è attivo e il totale dell'ordine (prima della commissione) sta nei limiti impostati.</summary>
    public bool Disponibile(decimal totaleSenzaCommissione) =>
        Attivo && (OrdineMinimo is not { } minimo || totaleSenzaCommissione >= minimo) && (OrdineMassimo is not { } massimo || totaleSenzaCommissione <= massimo);

    /// <summary>
    /// Commissione lorda (IVA inclusa): zero dal totale <see cref="CommissioneGratisSopra"/> in su; altrimenti la parte fissa più la percentuale
    /// del totale (articoli e spedizione, prima della commissione), arrotondata al centesimo.
    /// </summary>
    public decimal Commissione(decimal totaleSenzaCommissione)
    {
        if (CommissioneGratisSopra is { } soglia && totaleSenzaCommissione >= soglia)
        {
            return 0m;
        }

        return CommissioneFissa + decimal.Round(totaleSenzaCommissione * CommissionePercentuale / 100m, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Regole del contrassegno (ADR-0023). Vale solo per la consegna in Italia (metodo a zona, area Italia): è un servizio dei corrieri
/// nazionali e non si offre per il ritiro in sede né per l'estero.
/// </summary>
public static class RegoleContrassegno
{
    /// <summary>Vero se il metodo di consegna ammette il contrassegno.</summary>
    public static bool MetodoAmmesso(TipoMetodoSpedizione tipo, AreaSpedizione? area) => tipo == TipoMetodoSpedizione.Zona && area == AreaSpedizione.Italia;
}

/// <summary>Causale del bonifico: identifica l'ordine per chi riconcilia i movimenti in banca. Non si traduce.</summary>
public static class CausaleBonifico
{
    public static string Per(int numeroOrdine) => string.Create(CultureInfo.InvariantCulture, $"Ordine {numeroOrdine}");
}

/// <summary>IBAN: normalizzazione e controllo formale (struttura e cifre di controllo ISO 13616, modulo 97). Non verifica che il conto esista.</summary>
public static partial class Iban
{
    public static string? Normalizza(string? valore)
    {
        if (string.IsNullOrWhiteSpace(valore))
        {
            return null;
        }

        return new string([.. valore.Where(c => !char.IsWhiteSpace(c))]).ToUpperInvariant();
    }

    public static bool Valido(string? valore)
    {
        var iban = Normalizza(valore);
        if (iban is null || iban.Length is < 15 or > 34 || !Struttura().IsMatch(iban))
        {
            return false;
        }

        // Italia: lunghezza fissa di 27 caratteri.
        if (iban.StartsWith("IT", StringComparison.Ordinal) && iban.Length != 27)
        {
            return false;
        }

        // Si sposta l'intestazione in coda e si sostituiscono le lettere con due cifre (A=10 ... Z=35): il resto della divisione per 97 deve essere 1.
        var riordinato = iban[4..] + iban[..4];
        var cifre = new System.Text.StringBuilder();
        foreach (var carattere in riordinato)
        {
            cifre.Append(char.IsDigit(carattere) ? carattere.ToString() : (carattere - 'A' + 10).ToString(CultureInfo.InvariantCulture));
        }

        return BigInteger.Parse(cifre.ToString(), CultureInfo.InvariantCulture) % 97 == 1;
    }

    /// <summary>Gruppi di quattro caratteri separati da uno spazio, per mostrarlo al cliente.</summary>
    public static string Formatta(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        return string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - (i * 4)))));
    }

    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Struttura();
}

/// <summary>BIC/SWIFT: controllo formale (8 o 11 caratteri).</summary>
public static partial class CodiceBic
{
    public static string? Normalizza(string? valore) => string.IsNullOrWhiteSpace(valore) ? null : new string([.. valore.Where(c => !char.IsWhiteSpace(c))]).ToUpperInvariant();

    public static bool Valido(string? valore) => Normalizza(valore) is { } bic && Formato().IsMatch(bic);

    [GeneratedRegex("^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();
}

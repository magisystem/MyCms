namespace DvCms.Domain.Negozio;

/// <summary>
/// Stati dell'ordine (ADR-0020, <c>docs/architecture/ecommerce.md</c>). Le transizioni consentite sono in <see cref="TransizioniOrdine"/>.
/// I valori sono salvati come testo: non rinominarli. Gli stati di rimborso si aggiungono quando sarà approvata la politica A5.
/// </summary>
public enum StatoOrdine
{
    InAttesaDiPagamento = 1,
    PagamentoFallito = 2,
    Pagato = 3,

    /// <summary>Pagato e spedito all'indirizzo (metodo a zona). Finale.</summary>
    Spedito = 4,

    /// <summary>Pagato e ritirato in sede (metodo ritiro). Finale.</summary>
    Ritirato = 5,

    /// <summary>Annullato prima del pagamento. Finale.</summary>
    Annullato = 6,
}

/// <summary>
/// Macchina a stati dell'ordine: l'unico posto che dice quali passaggi sono leciti. Ogni cambio di stato nel codice passa da qui;
/// un passaggio non elencato è un errore del programma, non un'opzione.
/// </summary>
public static class TransizioniOrdine
{
    public static IReadOnlyList<StatoOrdine> Successivi(StatoOrdine da) => da switch
    {
        StatoOrdine.InAttesaDiPagamento => [StatoOrdine.Pagato, StatoOrdine.PagamentoFallito, StatoOrdine.Annullato],
        StatoOrdine.PagamentoFallito => [StatoOrdine.InAttesaDiPagamento, StatoOrdine.Annullato],
        StatoOrdine.Pagato => [StatoOrdine.Spedito, StatoOrdine.Ritirato],
        _ => [],
    };

    /// <summary>Vero se il passaggio è previsto. <paramref name="tipoSpedizione"/> distingue <c>Spedito</c> (solo a zona) da <c>Ritirato</c> (solo ritiro).</summary>
    public static bool Consentita(StatoOrdine da, StatoOrdine a, TipoMetodoSpedizione tipoSpedizione)
    {
        if (!Successivi(da).Contains(a))
        {
            return false;
        }

        return a switch
        {
            StatoOrdine.Spedito => tipoSpedizione == TipoMetodoSpedizione.Zona,
            StatoOrdine.Ritirato => tipoSpedizione == TipoMetodoSpedizione.Ritiro,
            _ => true,
        };
    }

    public static bool EFinale(StatoOrdine stato) => Successivi(stato).Count == 0;

    /// <summary>Stati in cui il cliente può ancora annullare l'ordine da solo (nessun pagamento incassato).</summary>
    public static bool AnnullabileDalCliente(StatoOrdine stato) => stato is StatoOrdine.InAttesaDiPagamento or StatoOrdine.PagamentoFallito;
}

/// <summary>
/// Ordine di un acquirente (F-003). Tutto ciò che conta è una **copia** dei valori al momento dell'acquisto — importi, nomi, codici,
/// metodo di spedizione, indirizzo, email — perché catalogo, tariffe e account possono cambiare o sparire senza toccare lo storico.
/// Non ha chiavi verso varianti o metodi di spedizione. I totali comprendono la spedizione; <c>Lordo = Imponibile + Iva</c> è un
/// vincolo del database.
/// </summary>
public class Ordine
{
    public const int LunghezzaMassimaIndirizzo = 200;
    public const int LunghezzaMassimaDestinatario = 150;
    public const int LunghezzaMassimaCitta = 100;
    public const int LunghezzaMassimaCap = 10;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    /// <summary>Progressivo dell'ordine nel sito, senza buchi (si assegna nella stessa transazione che crea l'ordine).</summary>
    public int Numero { get; set; }

    public Guid IdUtenza { get; set; }

    public StatoOrdine Stato { get; set; } = StatoOrdine.InAttesaDiPagamento;

    /// <summary>Lingua in cui il cliente ha acquistato (di una lingua abilitata del sito): è quella dei nomi nello snapshot e delle email sull'ordine.</summary>
    public string CodiceLingua { get; set; } = "it";

    public string Valuta { get; set; } = VarianteProdotto.ValutaPredefinita;

    public decimal TotaleLordo { get; set; }

    public decimal TotaleImponibile { get; set; }

    public decimal TotaleIva { get; set; }

    // Copia del metodo di spedizione scelto e del suo importo (IVA ordinaria scorporata).
    public TipoMetodoSpedizione TipoSpedizione { get; set; }

    public AreaSpedizione? AreaSpedizione { get; set; }

    public string NomeSpedizione { get; set; } = string.Empty;

    public decimal SpedizioneLorda { get; set; }

    public decimal SpedizioneImponibile { get; set; }

    public decimal SpedizioneIva { get; set; }

    // Copia di chi ordina.
    public string EmailAcquirente { get; set; } = string.Empty;

    public string NomeAcquirente { get; set; } = string.Empty;

    // Indirizzo di consegna: assente per il ritiro in sede.
    public string? Destinatario { get; set; }

    public string? IndirizzoRiga1 { get; set; }

    public string? IndirizzoRiga2 { get; set; }

    public string? Cap { get; set; }

    public string? Citta { get; set; }

    /// <summary>Codice ISO 3166-1 a due lettere maiuscole.</summary>
    public string? Paese { get; set; }

    /// <summary>Fino a quando la giacenza è trattenuta per questo ordine; nulla se nessun articolo ha giacenza gestita.</summary>
    public DateTime? PrenotazioneScadeIlUtc { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

public class RigaOrdine
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdOrdine { get; set; }

    public int Posizione { get; set; }

    /// <summary>Variante ordinata al momento dell'acquisto: riferimento senza vincolo (la variante può sparire dal catalogo); serve alla giacenza.</summary>
    public Guid IdVariante { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string NomeProdotto { get; set; } = string.Empty;

    public string? EtichettaVariante { get; set; }

    public int Quantita { get; set; }

    public decimal PrezzoUnitarioLordo { get; set; }

    public decimal AliquotaIva { get; set; }

    public decimal Lordo { get; set; }

    public decimal Imponibile { get; set; }

    public decimal Iva { get; set; }
}

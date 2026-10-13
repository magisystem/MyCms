using DvCms.DTO.Email;
using DvCms.Models.Localizzazione;

namespace DvCms.Emails;

/// <summary>
/// Il catalogo dei modelli email (ADR-0025): quali sono, quali variabili offrono e i modelli di fabbrica in italiano e inglese. Chi
/// manda un'email indica un codice di questo catalogo; il tenant può sostituire il modello di fabbrica, lingua per lingua.
/// </summary>
public static class CatalogoModelliEmail
{
    private static readonly VariabileModelloEmail[] _dettaglioOrdine =
    [
        new("numero", "Numero dell'ordine."),
        new("nome_sito", "Nome del sito."),
        new("righe", "Elenco delle righe: ognuna ha nome, codice, quantita e importo."),
        new("consegna_nome", "Nome del metodo di consegna."),
        new("consegna_importo", "Costo della consegna."),
        new("commissione", "Commissione di contrassegno; vuota se non c'è."),
        new("totale", "Totale dell'ordine, IVA inclusa."),
        new("ritiro", "Vero se l'ordine si ritira in sede."),
        new("indirizzo", "Elenco delle righe dell'indirizzo di consegna, destinatario compreso."),
        new("link_ordine", "Indirizzo della pagina dell'ordine."),
    ];

    private static readonly DefinizioneModelloEmail[] _definizioni =
    [
        new(
            CodiciModelloEmail.RecuperoCodice,
            "Recupero password: codice",
            "Il codice a sei cifre che serve a reimpostare la password. Scade in pochi minuti.",
            [
                new("nome", "Nome di chi riceve l'email."),
                new("codice", "Il codice di verifica.", Obbligatoria: true),
                new("minuti_validita", "Minuti di validità del codice."),
            ]),
        new(
            CodiciModelloEmail.RecuperoLink,
            "Recupero password: link",
            "Il link per scegliere una nuova password, inviato dopo aver inserito il codice.",
            [
                new("nome", "Nome di chi riceve l'email."),
                new("link", "Il link per reimpostare la password.", Obbligatoria: true),
                new("minuti_validita", "Minuti di validità del link."),
            ]),
        new(
            CodiciModelloEmail.RecuperoNotifica,
            "Recupero password: avviso di cambio",
            "L'avviso che la password è stata cambiata.",
            [new("nome", "Nome di chi riceve l'email.")]),
        new(
            CodiciModelloEmail.ConfermaRegistrazione,
            "Conferma della registrazione",
            "Il link con cui un cliente conferma la registrazione al sito e sceglie la password.",
            [
                new("nome", "Nome di chi riceve l'email."),
                new("nome_sito", "Nome del sito."),
                new("link", "Il link di conferma.", Obbligatoria: true),
                new("ore_validita", "Ore di validità del link."),
            ]),
        new(
            CodiciModelloEmail.InvitoAmministratore,
            "Invito come amministratore",
            "L'invito a gestire il sito, con il link per scegliere la password.",
            [
                new("nome", "Nome di chi riceve l'email."),
                new("nome_tenant", "Nome del tenant."),
                new("link", "Il link dell'invito.", Obbligatoria: true),
                new("ore_validita", "Ore di validità del link."),
            ]),
        new(
            CodiciModelloEmail.OrdineConfermato,
            "Ordine confermato (pagato)",
            "Al cliente, quando il pagamento di un ordine è stato ricevuto.",
            _dettaglioOrdine),
        new(
            CodiciModelloEmail.OrdineRicevutoBonifico,
            "Ordine ricevuto: istruzioni per il bonifico",
            "Al cliente che ha scelto il bonifico: coordinate, importo, causale e termine.",
            [
                .. _dettaglioOrdine,
                new("importo", "Importo da bonificare."),
                new("intestatario", "Intestatario del conto."),
                new("iban", "IBAN."),
                new("bic", "BIC/SWIFT; vuoto se non c'è."),
                new("banca", "Nome della banca; vuoto se non c'è."),
                new("causale", "Causale del bonifico."),
                new("scadenza", "Data entro cui bonificare; vuota se non c'è."),
                new("istruzioni", "Istruzioni aggiuntive del tenant; vuote se non ci sono."),
            ]),
        new(
            CodiciModelloEmail.OrdineRicevutoContrassegno,
            "Ordine ricevuto: contrassegno",
            "Al cliente che ha scelto il contrassegno: l'importo da pagare alla consegna.",
            [
                .. _dettaglioOrdine,
                new("importo", "Importo da pagare alla consegna."),
                new("istruzioni", "Istruzioni aggiuntive del tenant; vuote se non ci sono."),
            ]),
        new(
            CodiciModelloEmail.OrdineNuovoAmministratore,
            "Nuovo ordine (avviso agli amministratori)",
            "Agli amministratori del tenant, quando arriva un nuovo ordine.",
            [
                new("nome", "Nome dell'amministratore che riceve l'avviso."),
                new("numero", "Numero dell'ordine."),
                new("nome_sito", "Nome del sito."),
                new("tipo", "Come si paga: pagato, bonifico o contrassegno."),
                new("importo", "Totale dell'ordine."),
                new("articoli", "Numero di articoli."),
                new("cliente_nome", "Nome del cliente."),
                new("cliente_email", "Email del cliente."),
                new("consegna_nome", "Nome del metodo di consegna."),
                new("link_ordine", "Indirizzo della pagina di gestione dell'ordine."),
            ]),
        new(
            CodiciModelloEmail.Rimborso,
            "Rimborso",
            "Al cliente, quando un rimborso è concluso.",
            [
                new("numero", "Numero dell'ordine."),
                new("nome_sito", "Nome del sito."),
                new("importo", "Importo rimborsato."),
                new("totale", "Vero se il rimborso è per l'intero ordine."),
                new("motivo", "Motivo del rimborso; vuoto se non indicato."),
                new("link_ordine", "Indirizzo della pagina dell'ordine."),
            ]),
        new(
            CodiciModelloEmail.Prova,
            "Email di prova",
            "Il messaggio di prova che si manda dalla pagina delle impostazioni email.",
            []),
    ];

    private static readonly Dictionary<string, DefinizioneModelloEmail> _perCodice = _definizioni.ToDictionary(d => d.Codice, StringComparer.Ordinal);

    /// <summary>Le lingue in cui esistono modelli di fabbrica.</summary>
    public static IReadOnlyList<string> LingueDiFabbrica { get; } = [Lingua.CodiceItaliano, Lingua.CodiceInglese];

    public static IReadOnlyList<DefinizioneModelloEmail> Definizioni => _definizioni;

    public static DefinizioneModelloEmail? Trova(string codice) => codice is not null && _perCodice.TryGetValue(codice, out var definizione) ? definizione : null;

    /// <summary>Il modello di fabbrica per la lingua, oppure <c>null</c> se per quella lingua non ce n'è.</summary>
    public static ContenutoModelloEmail? Fabbrica(string codice, string lingua) =>
        ModelliDiFabbrica.Contenuti.TryGetValue((codice, lingua), out var contenuto) ? contenuto : null;

    /// <summary>I valori d'esempio con cui si controlla e si mostra in anteprima un modello. Le variabili obbligatorie hanno valori riconoscibili.</summary>
    public static IReadOnlyDictionary<string, object?> Esempio(string codice)
    {
        var dettaglio = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["numero"] = "1042",
            ["nome_sito"] = "Bottega Esempio",
            ["righe"] = new List<object?>
            {
                new Dictionary<string, object?> { ["nome"] = "Tazza blu", ["codice"] = "TZ-BLU", ["quantita"] = 2, ["importo"] = "24,00 EUR" },
                new Dictionary<string, object?> { ["nome"] = "Piatto grande — verde", ["codice"] = "PT-GR-V", ["quantita"] = 1, ["importo"] = "18,50 EUR" },
            },
            ["consegna_nome"] = "Corriere espresso",
            ["consegna_importo"] = "6,90 EUR",
            ["commissione"] = null,
            ["totale"] = "49,40 EUR",
            ["ritiro"] = false,
            ["indirizzo"] = new List<object?> { "Mario Rossi", "Via Roma 1", "00100 Roma (IT)" },
            ["link_ordine"] = "https://www.esempio.it/account/ordini/2f6b1c0e",
        };

        var valori = new Dictionary<string, object?>(StringComparer.Ordinal) { ["nome"] = "Mario Rossi" };
        switch (codice)
        {
            case CodiciModelloEmail.RecuperoCodice:
                valori["codice"] = "483920";
                valori["minuti_validita"] = 15;
                break;
            case CodiciModelloEmail.RecuperoLink:
                valori["link"] = "https://www.esempio.it/admin/recupero/reimposta?token=ESEMPIO7Q2";
                valori["minuti_validita"] = 30;
                break;
            case CodiciModelloEmail.ConfermaRegistrazione:
                valori["nome_sito"] = "Bottega Esempio";
                valori["link"] = "https://www.esempio.it/account/conferma?token=ESEMPIO7Q2";
                valori["ore_validita"] = 24;
                break;
            case CodiciModelloEmail.InvitoAmministratore:
                valori["nome_tenant"] = "Bottega Esempio";
                valori["link"] = "https://www.esempio.it/admin/invito?token=ESEMPIO7Q2";
                valori["ore_validita"] = 72;
                break;
            case CodiciModelloEmail.OrdineConfermato:
                return dettaglio;
            case CodiciModelloEmail.OrdineRicevutoBonifico:
                dettaglio["importo"] = "49,40 EUR";
                dettaglio["intestatario"] = "Bottega Esempio S.r.l.";
                dettaglio["iban"] = "IT60 X054 2811 1010 0000 0123 456";
                dettaglio["bic"] = "BPMOIT22XXX";
                dettaglio["banca"] = "Banca Esempio";
                dettaglio["causale"] = "Ordine 1042";
                dettaglio["scadenza"] = "20/10/2026";
                dettaglio["istruzioni"] = "Invia la ricevuta a ordini@esempio.it.";
                return dettaglio;
            case CodiciModelloEmail.OrdineRicevutoContrassegno:
                dettaglio["importo"] = "49,40 EUR";
                dettaglio["istruzioni"] = "Tieni il contante a portata di mano.";
                return dettaglio;
            case CodiciModelloEmail.OrdineNuovoAmministratore:
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["nome"] = "Mario Rossi",
                    ["numero"] = "1042",
                    ["nome_sito"] = "Bottega Esempio",
                    ["tipo"] = "pagato",
                    ["importo"] = "49,40 EUR",
                    ["articoli"] = 3,
                    ["cliente_nome"] = "Mario Rossi",
                    ["cliente_email"] = "mario.rossi@esempio.it",
                    ["consegna_nome"] = "Corriere espresso",
                    ["link_ordine"] = "https://www.esempio.it/admin/negozio/ordini/2f6b1c0e",
                };
            case CodiciModelloEmail.Rimborso:
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["numero"] = "1042",
                    ["nome_sito"] = "Bottega Esempio",
                    ["importo"] = "18,50 EUR",
                    ["totale"] = false,
                    ["motivo"] = "Articolo danneggiato",
                    ["link_ordine"] = "https://www.esempio.it/account/ordini/2f6b1c0e",
                };
        }

        return valori;
    }
}

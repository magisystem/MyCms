using System.Security.Cryptography;
using System.Text;
using DvCms.DTO.Builder;

namespace DvCms.Core.Builder;

/// <summary>Registro dei template (ADR-0005): tutte le versioni rilasciate, immutabili. L'id del template è l'id del modello di pagina.</summary>
public sealed class RegistroTemplateBuilder
{
    public const string PaginaLibera = "pagina-libera";
    public const string IntestazioneFissa = "pagina-con-intestazione-fissa";

    private readonly Dictionary<(string Id, int Versione), TemplateBuilder> _template;

    public RegistroTemplateBuilder(IEnumerable<TemplateBuilder> template)
    {
        ArgumentNullException.ThrowIfNull(template);

        _template = [];
        foreach (var t in template)
        {
            if (!_template.TryAdd((t.Id, t.Versione), t))
            {
                throw new ArgumentException($"Template '{t.Id}' versione {t.Versione} definito due volte.", nameof(template));
            }
        }
    }

    /// <summary>Pagina libera: la radice è uno slot che accetta solo sezioni; ogni cosa sta dentro una sezione.</summary>
    public static TemplateBuilder TemplatePaginaLibera { get; } = new(PaginaLibera, 1, [], new SlotTemplate(["layout.sezione"], 20));

    /// <summary>
    /// Pagina con intestazione fissa: l'intestazione (di cui si cambia solo il testo del titolo) e l'avviso in fondo sono bloccati;
    /// il corpo è uno slot che accetta contenuti di base e colonne.
    /// </summary>
    public static TemplateBuilder TemplateIntestazioneFissa { get; } = CreaIntestazioneFissa(
        1,
        ["layout.colonne", "contenuto.titolo", "contenuto.testo", "contenuto.immagine", "contenuto.pulsante"]);

    /// <summary>
    /// Versione 2 (M5): stessa struttura, ma il corpo accetta anche i componenti avanzati (hero, invito all'azione, fisarmonica).
    /// Un documento della versione 1 è valido anche per la 2, quindi il passaggio è possibile.
    /// </summary>
    public static TemplateBuilder TemplateIntestazioneFissaV2 { get; } = CreaIntestazioneFissa(
        2,
        ["layout.colonne", "contenuto.titolo", "contenuto.testo", "contenuto.immagine", "contenuto.pulsante", "contenuto.hero", "contenuto.cta", "contenuto.fisarmonica"]);

    /// <summary>Versione 3 (M5): il corpo ammette anche il testo con formattazione. I documenti della v1 e v2 restano validi.</summary>
    public static TemplateBuilder TemplateIntestazioneFissaV3 { get; } = CreaIntestazioneFissa(
        3,
        ["layout.colonne", "contenuto.titolo", "contenuto.testo", "contenuto.testo-ricco", "contenuto.immagine", "contenuto.pulsante", "contenuto.hero", "contenuto.cta", "contenuto.fisarmonica"]);

    /// <summary>Versione 4 (M5): il corpo ammette anche griglia di caratteristiche e testimonianze. Le versioni precedenti restano valide.</summary>
    public static TemplateBuilder TemplateIntestazioneFissaV4 { get; } = CreaIntestazioneFissa(
        4,
        ["layout.colonne", "contenuto.titolo", "contenuto.testo", "contenuto.testo-ricco", "contenuto.immagine", "contenuto.pulsante", "contenuto.hero", "contenuto.cta", "contenuto.fisarmonica", "contenuto.caratteristiche", "contenuto.testimonianze"]);

    /// <summary>Versione 5 (M7): il corpo ammette anche la griglia di prodotti del negozio. Le versioni precedenti restano valide.</summary>
    public static TemplateBuilder TemplateIntestazioneFissaV5 { get; } = CreaIntestazioneFissa(
        5,
        ["layout.colonne", "contenuto.titolo", "contenuto.testo", "contenuto.testo-ricco", "contenuto.immagine", "contenuto.pulsante", "contenuto.hero", "contenuto.cta", "contenuto.fisarmonica", "contenuto.caratteristiche", "contenuto.testimonianze", "negozio.griglia-prodotti"]);

    /// <summary>Versione 6 (M7): il corpo ammette anche la scheda di un prodotto scelto. Le versioni precedenti restano valide.</summary>
    public static TemplateBuilder TemplateIntestazioneFissaV6 { get; } = CreaIntestazioneFissa(
        6,
        ["layout.colonne", "contenuto.titolo", "contenuto.testo", "contenuto.testo-ricco", "contenuto.immagine", "contenuto.pulsante", "contenuto.hero", "contenuto.cta", "contenuto.fisarmonica", "contenuto.caratteristiche", "contenuto.testimonianze", "negozio.griglia-prodotti", "negozio.scheda-prodotto"]);

    /// <summary>Versione 7 (M7): il corpo ammette anche la griglia dei prodotti di una categoria. Le versioni precedenti restano valide.</summary>
    public static TemplateBuilder TemplateIntestazioneFissaV7 { get; } = CreaIntestazioneFissa(
        7,
        ["layout.colonne", "contenuto.titolo", "contenuto.testo", "contenuto.testo-ricco", "contenuto.immagine", "contenuto.pulsante", "contenuto.hero", "contenuto.cta", "contenuto.fisarmonica", "contenuto.caratteristiche", "contenuto.testimonianze", "negozio.griglia-prodotti", "negozio.scheda-prodotto", "negozio.griglia-categoria"]);

    private static TemplateBuilder CreaIntestazioneFissa(int versione, IReadOnlyList<string> tipiNelCorpo) => new(
        IntestazioneFissa,
        versione,
        [
            new NodoTemplate(
                "intestazione",
                "layout.sezione",
                Stili: new Dictionary<string, string> { ["sfondo"] = "scuro", ["spaziatura"] = "ampia" },
                Figli:
                [
                    new NodoTemplate(
                        "intestazione-titolo",
                        "contenuto.titolo",
                        Proprieta: new Dictionary<string, string> { ["testo"] = "Titolo della pagina", ["livello"] = "1" },
                        Stili: new Dictionary<string, string> { ["allineamento"] = "centro" },
                        ProprietaModificabili: ["testo"]),
                ]),
            new NodoTemplate(
                "corpo",
                "layout.sezione",
                Figli:
                [
                    new NodoTemplate("corpo-testo", "contenuto.testo", Proprieta: new Dictionary<string, string> { ["testo"] = "Scrivi qui il contenuto della pagina." }),
                ],
                Slot: new SlotTemplate(tipiNelCorpo, 30)),
            new NodoTemplate(
                "avviso",
                "layout.sezione",
                Stili: new Dictionary<string, string> { ["sfondo"] = "chiaro", ["spaziatura"] = "compatta" },
                Figli:
                [
                    new NodoTemplate("avviso-testo", "contenuto.testo", Proprieta: new Dictionary<string, string> { ["testo"] = "Le informazioni di questa pagina sono fornite a scopo illustrativo." }, Stili: new Dictionary<string, string> { ["allineamento"] = "centro" }),
                ]),
        ]);

    public static IReadOnlyList<TemplateBuilder> Predefiniti { get; } = [TemplatePaginaLibera, TemplateIntestazioneFissa, TemplateIntestazioneFissaV2, TemplateIntestazioneFissaV3, TemplateIntestazioneFissaV4, TemplateIntestazioneFissaV5, TemplateIntestazioneFissaV6, TemplateIntestazioneFissaV7];

    public IReadOnlyList<TemplateBuilder> Tutti => [.. _template.Values.OrderBy(t => t.Id, StringComparer.Ordinal).ThenBy(t => t.Versione)];

    /// <summary>L'ultima versione rilasciata di un template.</summary>
    public TemplateBuilder? Ultimo(string? id) =>
        id is null ? null : _template.Values.Where(t => t.Id == id).MaxBy(t => t.Versione);

    public TemplateBuilder? Trova(string? id, int versione) =>
        id is not null && _template.TryGetValue((id, versione), out var t) ? t : null;
}

/// <summary>
/// Policy di editing lato server (E-004): confronta il documento inviato con il template e rifiuta ogni modifica a ciò che è
/// bloccato, qualunque cosa dica il browser. Non si fida di alcun campo "locked" del client: conta solo la struttura del template.
/// </summary>
public static class ValidatoreTemplate
{
    public static IReadOnlyList<ErroreBuilder> Valida(TemplateBuilder template, DocumentoCms documento)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(documento);

        var errori = new List<ErroreBuilder>();
        if (template.SlotRadice is { } slotRadice)
        {
            ValidaSlot(documento.Nodi, slotRadice, null, errori);
        }
        else
        {
            ConfrontaElenco(template.Radice, documento.Nodi, null, errori);
        }

        return errori;
    }

    private static void ConfrontaElenco(IReadOnlyList<NodoTemplate> attesi, IReadOnlyList<NodoCms> trovati, string? idGenitore, List<ErroreBuilder> errori)
    {
        foreach (var atteso in attesi.Where(a => trovati.All(t => t.Id != a.Id)))
        {
            errori.Add(new ErroreBuilder("NodoBloccatoMancante", atteso.Id));
        }

        foreach (var extra in trovati.Where(t => attesi.All(a => a.Id != t.Id)))
        {
            errori.Add(new ErroreBuilder("NodoFuoriSlot", Taglia(extra.Id), idGenitore));
        }

        // L'ordine dei nodi bloccati è parte della struttura: spostarli è una modifica.
        var attesiPresenti = attesi.Where(a => trovati.Any(t => t.Id == a.Id)).Select(a => a.Id).ToList();
        var trovatiPrevisti = trovati.Where(t => attesi.Any(a => a.Id == t.Id)).Select(t => t.Id).ToList();
        if (!attesiPresenti.SequenceEqual(trovatiPrevisti, StringComparer.Ordinal))
        {
            errori.Add(new ErroreBuilder("NodoBloccatoSpostato", idGenitore));
        }

        foreach (var atteso in attesi)
        {
            if (trovati.FirstOrDefault(t => t.Id == atteso.Id) is { } nodo)
            {
                ConfrontaNodo(atteso, nodo, errori);
            }
        }
    }

    private static void ConfrontaNodo(NodoTemplate atteso, NodoCms nodo, List<ErroreBuilder> errori)
    {
        if (!string.Equals(atteso.Tipo, nodo.Tipo, StringComparison.Ordinal))
        {
            errori.Add(new ErroreBuilder("NodoBloccatoModificato", atteso.Id, "tipo"));
            return;
        }

        ConfrontaValori(atteso.Id, "proprieta", atteso.Proprieta, nodo.Proprieta, atteso.ProprietaModificabili, errori);
        ConfrontaValori(atteso.Id, "stili", atteso.Stili, nodo.Stili, null, errori);

        if (atteso.Slot is { } slot)
        {
            ValidaSlot(nodo.Figli, slot, atteso.Id, errori);
        }
        else
        {
            ConfrontaElenco(atteso.Figli ?? [], nodo.Figli, atteso.Id, errori);
        }
    }

    private static void ConfrontaValori(
        string idNodo,
        string campo,
        IReadOnlyDictionary<string, string>? attesi,
        IReadOnlyDictionary<string, string> trovati,
        IReadOnlyList<string>? modificabili,
        List<ErroreBuilder> errori)
    {
        attesi ??= new Dictionary<string, string>();
        var nomi = attesi.Keys.Concat(trovati.Keys).Distinct(StringComparer.Ordinal);
        foreach (var nome in nomi.Where(n => modificabili?.Contains(n, StringComparer.Ordinal) != true))
        {
            var a = attesi.TryGetValue(nome, out var va) ? va : string.Empty;
            var t = trovati.TryGetValue(nome, out var vt) ? vt : string.Empty;
            if (!string.Equals(a, t, StringComparison.Ordinal))
            {
                errori.Add(new ErroreBuilder("NodoBloccatoModificato", idNodo, $"{campo}.{(nome.Length > 40 ? nome[..40] : nome)}"));
            }
        }
    }

    private static void ValidaSlot(IReadOnlyList<NodoCms> figli, SlotTemplate slot, string? idSlot, List<ErroreBuilder> errori)
    {
        if (figli.Count > slot.FigliMassimi)
        {
            errori.Add(new ErroreBuilder("TroppiFigliNelloSlot", idSlot));
        }

        foreach (var figlio in figli.Where(f => !slot.TipiAmmessi.Contains(f.Tipo, StringComparer.Ordinal)))
        {
            errori.Add(new ErroreBuilder("TipoNonAmmessoNelloSlot", Taglia(figlio.Id), idSlot));
        }
    }

    private static string Taglia(string id) => id.Length > RegistroComponenti.LunghezzaMassimaId ? id[..RegistroComponenti.LunghezzaMassimaId] : id;
}

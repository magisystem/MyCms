using System.Text.Json;
using DvCms.Application.Builder;
using DvCms.Application.Localizzazione;
using DvCms.Application.Media;
using DvCms.Application.Piattaforma;
using DvCms.Application.Temi;
using DvCms.Web.Pubblico;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Pagine;

/// <summary>Corpo della richiesta di salvataggio: il token letto con la pagina e il progetto dell'editor.</summary>
public sealed record RichiestaSalvataggioBuilder(string? Token, string? Progetto, bool Automatico = false);

/// <summary>
/// Page builder (E-001): l'editor GrapesJS gira solo in questa pagina, come asset JavaScript isolato servito dalla stessa origine
/// (nessun CDN, nessuno script in linea). Questa pagina non contiene logica: il server fornisce lo stato (progetto, catalogo,
/// vincoli del template) e riceve il progetto da salvare, che riconverte, valida e riscrive da sé (<see cref="IServizioBuilder"/>).
/// </summary>
[RequestSizeLimit(AdapterGrapesJs.ByteMassimiProgetto + 65_536)]
public class BuilderModel : PaginaAdmin
{
    private readonly IServizioBuilder _builder;
    private readonly IServizioMedia _media;
    private readonly IRisolutoreTema _temi;

    public BuilderModel(IServizioBuilder builder, IServizioMedia media, IRisolutoreTema temi, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _builder = builder;
        _media = media;
        _temi = temi;
    }

    public Guid IdPagina { get; private set; }

    public string TestiJson => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["salva"] = Localizzatore.Traduci("Salva"),
        ["salvataggio"] = Localizzatore.Traduci("Salvataggio in corso…"),
        ["desktop"] = Localizzatore.Traduci("Desktop"),
        ["tablet"] = Localizzatore.Traduci("Tablet"),
        ["mobile"] = Localizzatore.Traduci("Mobile"),
        ["dispositivi"] = Localizzatore.Traduci("Dimensione dell'anteprima"),
        ["grassetto"] = Localizzatore.Traduci("Grassetto"),
        ["corsivo"] = Localizzatore.Traduci("Corsivo"),
        ["collegamento"] = Localizzatore.Traduci("Collegamento"),
        ["elenco"] = Localizzatore.Traduci("Elenco puntato"),
        ["indirizzoCollegamento"] = Localizzatore.Traduci("Indirizzo del collegamento (/percorso oppure https://…)"),
        ["markupNonValido"] = Localizzatore.Traduci("Formattazione non valida: controlla gli asterischi e i collegamenti."),
        ["salvato"] = Localizzatore.Traduci("Bozza salvata."),
        ["salvatoAutomatico"] = Localizzatore.Traduci("Salvato automaticamente"),
        ["automaticoSospeso"] = Localizzatore.Traduci("Salvataggio automatico sospeso: la pagina ha problemi da correggere."),
        ["nessunaModifica"] = Localizzatore.Traduci("Nessuna modifica da salvare."),
        ["torna"] = Localizzatore.Traduci("Torna alla pagina"),
        ["anteprima"] = Localizzatore.Traduci("Anteprima"),
        ["conflitto"] = Localizzatore.Traduci("La pagina è stata modificata da qualcun altro: ricarica e riprova."),
        ["erroreRete"] = Localizzatore.Traduci("Impossibile contattare il server."),
        ["erroreGenerico"] = Localizzatore.Traduci("Il salvataggio non è riuscito."),
        ["erroriValidazione"] = Localizzatore.Traduci("La pagina contiene problemi che impediscono il salvataggio:"),
        ["bloccato"] = Localizzatore.Traduci("Parte bloccata del modello: non si può modificare."),
        ["blocchi"] = Localizzatore.Traduci("Blocchi"),
        ["struttura"] = Localizzatore.Traduci("Struttura"),
        ["strutturaVuota"] = Localizzatore.Traduci("La pagina è vuota: inserisci un blocco."),
        ["bloccatoBreve"] = Localizzatore.Traduci("bloccato"),
        ["azioni"] = Localizzatore.Traduci("Azioni sul componente selezionato"),
        ["spostaSu"] = Localizzatore.Traduci("Sposta su"),
        ["spostaGiu"] = Localizzatore.Traduci("Sposta giù"),
        ["duplica"] = Localizzatore.Traduci("Duplica"),
        ["elimina"] = Localizzatore.Traduci("Elimina"),
        ["spostato"] = Localizzatore.Traduci("{0} spostato in posizione {1} di {2}."),
        ["duplicato"] = Localizzatore.Traduci("{0} duplicato."),
        ["eliminato"] = Localizzatore.Traduci("{0} eliminato."),
        ["selezionato"] = Localizzatore.Traduci("Selezionato: {0}"),
        ["inserisciBlocco"] = Localizzatore.Traduci("Inserisci {0}"),
        ["aiutoBlocchi"] = Localizzatore.Traduci("Invio o Spazio inserisce il blocco nel componente selezionato; con le frecce nella struttura si cambia selezione."),
        ["anteprimaPagina"] = Localizzatore.Traduci("Anteprima della pagina (da tastiera usa i blocchi, la struttura e le proprietà)"),
        ["vaiA"] = Localizzatore.Traduci("Vai a"),
        ["vaiBlocchi"] = Localizzatore.Traduci("Vai ai blocchi"),
        ["vaiStruttura"] = Localizzatore.Traduci("Vai alla struttura"),
        ["vaiProprieta"] = Localizzatore.Traduci("Vai alle proprietà"),
        ["annulla"] = Localizzatore.Traduci("Annulla"),
        ["ripeti"] = Localizzatore.Traduci("Ripeti"),
        ["cronologia"] = Localizzatore.Traduci("Cronologia delle modifiche"),
        ["proprieta"] = Localizzatore.Traduci("Proprietà"),
        ["nonSalvato"] = Localizzatore.Traduci("Modifiche non salvate."),
        ["nessunaRisorsa"] = Localizzatore.Traduci("(nessuna immagine)"),
        ["componenti"] = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["layout.sezione"] = Localizzatore.Traduci("Sezione"),
            ["layout.colonne"] = Localizzatore.Traduci("Colonne"),
            ["layout.colonna"] = Localizzatore.Traduci("Colonna"),
            ["contenuto.titolo"] = Localizzatore.Traduci("Titolo"),
            ["contenuto.testo"] = Localizzatore.Traduci("Testo"),
            ["contenuto.testo-ricco"] = Localizzatore.Traduci("Testo formattato"),
            ["contenuto.immagine"] = Localizzatore.Traduci("Immagine"),
            ["contenuto.pulsante"] = Localizzatore.Traduci("Pulsante"),
            ["contenuto.hero"] = Localizzatore.Traduci("Hero"),
            ["contenuto.cta"] = Localizzatore.Traduci("Invito all'azione"),
            ["contenuto.fisarmonica"] = Localizzatore.Traduci("Fisarmonica"),
            ["contenuto.voce"] = Localizzatore.Traduci("Voce"),
            ["contenuto.caratteristiche"] = Localizzatore.Traduci("Caratteristiche"),
            ["contenuto.caratteristica"] = Localizzatore.Traduci("Caratteristica"),
            ["contenuto.testimonianze"] = Localizzatore.Traduci("Testimonianze"),
            ["contenuto.testimonianza"] = Localizzatore.Traduci("Testimonianza"),
        }),
        ["campoObbligatorio"] = Localizzatore.Traduci("Campo obbligatorio"),
        ["gruppi"] = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["contenuto"] = Localizzatore.Traduci("Contenuto"),
            ["aspetto"] = Localizzatore.Traduci("Aspetto"),
            ["animazione"] = Localizzatore.Traduci("Animazione"),
        }),
        ["campi"] = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["testo"] = Localizzatore.Traduci("Testo"),
            ["livello"] = Localizzatore.Traduci("Livello"),
            ["idRisorsa"] = Localizzatore.Traduci("Immagine"),
            ["alt"] = Localizzatore.Traduci("Testo alternativo"),
            ["decorativa"] = Localizzatore.Traduci("Solo decorativa"),
            ["didascalia"] = Localizzatore.Traduci("Didascalia"),
            ["etichetta"] = Localizzatore.Traduci("Etichetta"),
            ["url"] = Localizzatore.Traduci("Indirizzo"),
            ["allineamento"] = Localizzatore.Traduci("Allineamento"),
            ["spaziatura"] = Localizzatore.Traduci("Spaziatura"),
            ["sfondo"] = Localizzatore.Traduci("Sfondo"),
            ["variante"] = Localizzatore.Traduci("Variante"),
            ["titolo"] = Localizzatore.Traduci("Titolo"),
            ["sottotitolo"] = Localizzatore.Traduci("Sottotitolo"),
            ["etichettaPulsante"] = Localizzatore.Traduci("Testo del pulsante"),
            ["urlPulsante"] = Localizzatore.Traduci("Indirizzo del pulsante"),
            ["domanda"] = Localizzatore.Traduci("Domanda"),
            ["risposta"] = Localizzatore.Traduci("Risposta"),
            ["descrizione"] = Localizzatore.Traduci("Descrizione"),
            ["citazione"] = Localizzatore.Traduci("Citazione"),
            ["autore"] = Localizzatore.Traduci("Autore"),
            ["ruolo"] = Localizzatore.Traduci("Ruolo"),
            ["colonne"] = Localizzatore.Traduci("Colonne"),
            ["altezza"] = Localizzatore.Traduci("Altezza"),
            ["animazione"] = Localizzatore.Traduci("Animazione"),
            ["ritardoAnimazione"] = Localizzatore.Traduci("Ritardo (ms)"),
        }),
    });

    public string RisorseJson { get; private set; } = "[]";

    /// <summary>
    /// Tema in vigore per il canvas: fogli (tema e skin dei componenti), classe e variabili del corpo, gli stessi della pagina pubblica,
    /// così l'editor mostra i componenti con l'aspetto che avranno. Sono dati del server: il browser non sceglie nulla.
    /// </summary>
    public string TemaJson { get; private set; } = "{}";

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        // Solo le pagine che usano un modello del builder: per le altre non c'è nulla da aprire.
        if (await _builder.LeggiAsync(IdAttore, id, annullamento) is null)
        {
            return NotFound();
        }

        IdPagina = id;
        var tema = await _temi.RisolviAsync(annullamento);
        var fogli = new List<string> { Url.Content(TemiVista.Foglio(tema)) };
        if (TemiVista.FoglioComponenti(tema) is { } skin)
        {
            fogli.Add(Url.Content(skin));
        }

        TemaJson = JsonSerializer.Serialize(new { fogli, classe = TemiVista.Classe(tema), variabili = tema.VariabiliCss });
        var risorse = await _media.ElencaAsync(IdAttore, annullamento);
        RisorseJson = JsonSerializer.Serialize(risorse
            .Where(r => r.TipoContenuto.StartsWith("image/", StringComparison.Ordinal) && r.Visibilita == Domain.Media.VisibilitaMedia.Pubblica)
            .Select(r => new { id = r.Id, nome = r.NomeOriginale, alt = r.TestoAlternativoPredefinito }));
        return Page();
    }

    /// <summary>Stato per l'editor: progetto da aprire, token di concorrenza, catalogo e vincoli del template.</summary>
    public async Task<IActionResult> OnGetStatoAsync(Guid id, CancellationToken annullamento)
    {
        Response.Headers.CacheControl = "no-store";
        var stato = await _builder.LeggiAsync(IdAttore, id, annullamento);
        return stato is null
            ? NotFound()
            : new JsonResult(new
            {
                titolo = stato.Titolo,
                template = stato.IdTemplate,
                versioneTemplate = stato.VersioneTemplate,
                numeroRevisione = stato.NumeroRevisione,
                token = stato.TokenConcorrenza,
                progetto = JsonDocument.Parse(stato.ProgettoEditorJson).RootElement,
                catalogo = JsonDocument.Parse(stato.CatalogoJson).RootElement,
                vincoli = JsonDocument.Parse(stato.VincoliJson).RootElement,
            });
    }

    /// <summary>
    /// Salvataggio: il corpo è il progetto così come lo produce l'editor. L'antiforgery è verificato dal framework (intestazione
    /// <c>RequestVerificationToken</c>); il resto lo decide il servizio.
    /// </summary>
    public async Task<IActionResult> OnPostSalvaAsync(Guid id, [FromBody] RichiestaSalvataggioBuilder richiesta, CancellationToken annullamento)
    {
        Response.Headers.CacheControl = "no-store";
        ArgumentNullException.ThrowIfNull(richiesta);

        var esito = await _builder.SalvaAsync(IdAttore, id, richiesta.Token, richiesta.Progetto, richiesta.Automatico, annullamento);
        var codiceHttp = esito.Codice switch
        {
            CodiceEsito.Riuscito => StatusCodes.Status200OK,
            CodiceEsito.Conflitto => StatusCodes.Status409Conflict,
            CodiceEsito.NonAutorizzato => StatusCodes.Status403Forbidden,
            CodiceEsito.NonTrovato => StatusCodes.Status404NotFound,
            CodiceEsito.NonConsentito => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return new JsonResult(new
        {
            ok = esito.Riuscito,
            errori = esito.Errori.Select(e => new { codice = e.Codice, idNodo = e.IdNodo, campo = e.Campo }),
            token = esito.TokenConcorrenza,
            progetto = esito.ProgettoEditorJson is { } progetto ? JsonDocument.Parse(progetto).RootElement : (JsonElement?)null,
            numeroRevisione = esito.NumeroRevisione,
            nuovaRevisione = esito.NuovaRevisione,
        })
        { StatusCode = codiceHttp };
    }
}

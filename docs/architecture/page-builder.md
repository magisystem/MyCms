# Page builder visuale

## Obiettivo

Offrire due esperienze sulla stessa piattaforma:

1. **Guidata:** template e form Razor con campi autorizzati; modalità predefinita per la maggioranza delle pagine.
2. **Libera:** editor visuale basato su GrapesJS, avviato da una pagina Razor dedicata. La libertà riguarda la composizione di componenti e proprietà supportate; non significa eseguire codice arbitrario del cliente.

## Tecnologia

GrapesJS viene caricato soltanto nella pagina editor. Può essere compilato come asset JS isolato usando una toolchain frontend (npm/bundler) se necessaria; non viene introdotto un backend Node. L'admin non diventa una SPA.

Prima del commit di dipendenze in un prodotto commerciale, verificare licenza/versione, dipendenze, manutenzione e modello di distribuzione di GrapesJS e degli eventuali plugin.

Riferimenti: [GrapesJS storage docs](https://grapesjs.com/docs/modules/Storage.html), [GrapesJS component docs](https://grapesjs.com/docs/modules/Components.html).

## Progetto editor e documento canonico

Persistire separatamente:

- `ProgettoEditorJson`: serializzazione richiesta per riaprire il progetto GrapesJS senza perdere componenti, stili, asset e metadata dell'editor.
- `DocumentoRenderJson`: documento CMS normalizzato, tipizzato e versionato usato dal renderer Razor.

Non usare l'HTML esportato come unica fonte di verità. GrapesJS stesso raccomanda il project JSON per ricaricare correttamente un progetto.

L'adapter `ProgettoEditor ↔ DocumentoCms` deve essere deterministico, versionato e coperto da test round-trip per i componenti supportati. Le conversioni possono essere lossy solo se il caso è esplicitamente escluso, mai silenziosamente.

## Schema iniziale del documento

```json
{
  "versioneSchema": 1,
  "nodi": [
    {
      "id": "hero-01",
      "tipo": "contenuto.hero",
      "proprieta": {
        "titolo": "Titolo della pagina",
        "sottotitolo": "Descrizione",
        "idRisorsaImmagine": "id-risorsa"
      },
      "stili": { "variante": "diviso", "spaziatura": "ampia" },
      "figli": []
    }
  ]
}
```

Questo è un esempio, non lo schema completo. Ogni tipo deve avere schema versionato, proprietà ammesse, default, regole di annidamento e validazione lato server.

## Famiglie di componenti iniziali

- Layout: `layout.sezione`, `layout.contenitore`, `layout.colonne`, `layout.griglia`.
- Contenuto: `contenuto.titolo`, `contenuto.testo-ricco`, `contenuto.immagine`, `contenuto.pulsante`, `contenuto.hero`, `contenuto.griglia-caratteristiche`, `contenuto.fisarmonica`, `contenuto.testimonianze`, `contenuto.cta`.
- Negozio: `negozio.griglia-prodotti`, `negozio.scheda-prodotto`, `negozio.dettaglio-prodotto`, `negozio.aggiungi-al-carrello`.

Il catalogo concreto va scelto per il primo tema/prototipo, senza implementare decine di componenti prima di validare il modello.

## Contratto componente

Ogni componente definisce:

- ID/tipo stabile e versione schema;
- schema delle proprietà e valori default;
- figli ammessi, cardinalità e regole di nesting;
- capabilities editor (inline edit, reorder, duplicate, delete, lock);
- renderer registrato;
- permessi richiesti;
- requisiti asset/dati dinamici;
- migrazione da versioni precedenti se necessaria.

## Policy template e aree bloccate

Un template versionato contiene struttura iniziale e policy di editing. Esempio di policy: proprietà modificabili, possibilità di spostare/eliminare un nodo, zone in cui aggiungere elementi e lista dei tipi consentiti.

La policy non è solo UI: il server deve convalidare il risultato salvato/pubblicato confrontandolo con la policy effettiva del template e i permessi dell'utente. Non fidarsi di campi `locked` inviati dal browser.

## Styling e animazioni

- Esprimere il più possibile layout e stile con token, varianti e proprietà allow-list.
- Supportare responsive breakpoint e opzioni grafiche esplicite.
- Animazioni tramite opzioni dichiarative implementate con CSS/JS fidato (es. fade-up, duration, delay, viewport trigger).
- Nessun JavaScript arbitrario nel documento CMS.
- HTML rich text ammesso solo dopo sanitizzazione con policy definita e testata.
- Non permettere URL `javascript:`, attributi event handler inline o script non autorizzati.

## Autosave, revisioni e concorrenza

- Il client può fare debounce dell'autosave; il server resta autorità.
- Tutti i salvataggi validano tenant, ruolo, pagina, policy template e payload.
- Usare token di concorrenza/ETag per evitare sovrascritture silenziose.
- Separare draft e published revision.
- Tracciare errori di migrazione/conversione senza perdere il progetto originale.

## Proof of concept obbligatorio

Prima di implementare il builder completo, realizzare un prototipo con:

1. una pagina template guidata;
2. una pagina libera usando soli componenti base;
3. un tema Razor;
4. salvataggio/ricaricamento del progetto GrapesJS;
5. conversione in render document e rendering pubblico;
6. un nodo bloccato e uno slot modificabile;
7. test round-trip e validazione di payload manipolato.

Se l'adapter produce perdite non accettabili, fermare l'espansione e aggiornare l'ADR del builder prima di sviluppare il catalogo completo.

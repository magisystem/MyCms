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

## Attuazione del proof of concept (M4)

Valutazione con criteri espliciti in [`docs/delivery/valutazione-poc-m4.md`](../delivery/valutazione-poc-m4.md). Qui il funzionamento.

- **Documento e registro (E-002).** `DocumentoCms` (`DvCms.Application.Builder`): `versioneSchema` 1 e nodi `id`/`tipo`/`proprieta`/`stili`/`figli`, in JSON canonico (`SerializzatoreDocumento`). `RegistroComponenti` è l'allow-list: sette componenti (`layout.sezione`, `layout.colonne` [2–3 colonne], `layout.colonna`, `contenuto.titolo`, `contenuto.testo`, `contenuto.immagine`, `contenuto.pulsante`); per ciascuno proprietà tipizzate (testo breve o lungo senza caratteri di controllo, indirizzo `/percorso` o `https://`, enumerazione, GUID di risorsa, sì/no), stili come enumerazioni (spaziatura, sfondo, allineamento, variante), figli ammessi e cardinalità. Limiti: profondità 6, 300 nodi, 256 KB di documento, 1 MB di progetto, 50 errori riportati. Gli errori hanno codice stabile, nodo e campo e **non contengono mai valori**. Un'impronta delle definizioni è fissata da un test.
- **Template e policy (E-004).** `TemplateBuilder` (versioni immutabili con impronta fissata): tutto ciò che il template definisce è bloccato (tipo, posizione, proprietà e stili, salvo le proprietà dichiarate modificabili); un nodo con uno **slot** accetta figli liberi solo dei tipi elencati e fino a un massimo; `pagina-libera` ha uno slot in radice (solo sezioni). `ValidatoreTemplate` confronta il documento con il template e restituisce `NodoBloccatoMancante`, `NodoBloccatoModificato`, `NodoBloccatoSpostato`, `NodoFuoriSlot`, `TipoNonAmmessoNelloSlot`, `TroppiFigliNelloSlot`. L'id del template è l'id del modello di pagina (`pagina-libera`, `pagina-con-intestazione-fissa`, con `Builder = true` e un'impronta propria).
- **Adapter (E-003).** `AdapterGrapesJs`: le proprietà e gli stili viaggiano come attributi `data-p-*` e `data-s-*` (nomi in kebab-case) dei componenti GrapesJS, il tipo è il `type` del componente. Si accettano solo tipi, attributi e metadati editoriali noti; tutto il resto (classi, stili in linea, regole CSS, componenti sconosciuti, più pagine, risorse) è un errore. ID mancanti: `auto-<percorso>` deterministico. Il progetto salvato è `AProgetto(documento)`: rigenerato, non copiato.
- **Salvataggio e concorrenza (E-005).** `IServizioBuilder`: attore riverificato sul database, token di concorrenza della pagina, conversione → normalizzazione → registro → policy del template → verifica delle risorse (devono essere pubbliche e del sito) → nuova revisione. Se il documento non cambia non si crea alcuna revisione. Un token vecchio dà `Conflitto` (409) senza scrivere. Il nuovo token torna nella risposta.
- **Pagina e endpoint (E-001).** `/admin/pagine/{id}/builder` (Razor Page, nessuno script in linea) con handler `Stato` (GET: progetto, catalogo, vincoli, token) e `Salva` (POST JSON con intestazione antiforgery `RequestVerificationToken`, limite di dimensione). Solo per le pagine con un modello del builder (404 per le altre e per i tenant diversi). Le pagine `Nuova` e `Modifica` rimandano al builder e non mostrano il testo libero.
- **Rendering.** `Modelli/_documento_v1` e `Componenti/_Nodo`: un `switch` sul tipo (allow-list), valori sempre codificati da Razor, enumerazioni e indirizzi ricontrollati in uscita, tipi sconosciuti ignorati; un JSON illeggibile non disegna nulla. Markup indipendente dal tema; stili in `wwwroot/css/componenti.css` (separato dai temi, che sono versioni immutabili), che leggono solo i token `--dv-*` del [contratto componenti-temi](componenti-e-temi.md); il canvas dell'editor carica gli stessi fogli (tema e skin) della pagina pubblica.
- **Client.** `frontend/page-builder` (vedi il suo README): tipi di componente, trait e blocchi generati dal catalogo del server; vincoli applicati all'interfaccia; errori del server mostrati con selezione del nodo.
- **Non incluso nel PoC:** vedi la sezione seguente per ciò che M5 ha aggiunto; restano fuori il rich text (poi aggiunto in M5) e i componenti del negozio (la griglia di prodotti è arrivata con M7.13).

## Page builder MVP (M5, in corso)

Primo incremento di M5; il resto (rich text con sanitizzazione, proprietà avanzate, componenti ulteriori) segue.

- **Componenti avanzati.** `contenuto.hero` (titolo, sottotitolo, pulsante facoltativo ma completo o assente, immagine di sfondo facoltativa, altezza, sfondo, allineamento), `contenuto.cta` (invito all'azione), `contenuto.fisarmonica` con le sue `contenuto.voce` (1–20; si disegnano con `<details>`: nessuno script, accessibili da tastiera). Le versioni di schema dei componenti modificati passano a 2; i documenti esistenti restano validi (le novità hanno tutte un default).
- **Animazioni dichiarative (E-006).** Ogni componente di contenuto e layout (non `layout.colonna` né `contenuto.voce`) ha due stili a elenco: `animazione` (`nessuna`, `dissolvenza`, `sale`, `da-sinistra`, `da-destra`) e `ritardoAnimazione` (0/150/300/600 ms). Il renderer emette solo classi da elenco (`dv-anim-*`, `dv-ritardo-*`); la logica è in `wwwroot/js/animazioni.js`, un file fidato del sito (IntersectionObserver), caricato in testa solo dalle pagine del builder. Lo stato nascosto vale solo se lo script ha marcato la pagina (`.dv-js`): senza JavaScript il contenuto è visibile; con `prefers-reduced-motion` non c'è alcuna animazione. Nessun JavaScript arriva dal documento.
- **Template v2.** `pagina-con-intestazione-fissa` v2 ammette i componenti avanzati nel corpo; la v1 resta immutabile. Le nuove pagine usano la v2; una pagina della v1 passa alla v2 con "Passa all'ultima versione del modello" **solo se il suo documento è valido anche per il nuovo template** (registro e policy), senza mai trasformarlo; altrimenti nulla cambia.
- **Salvataggio automatico controllato (E-005).** Il client salva dopo 5 secondi senza modifiche, non più spesso di ogni 15 secondi, si sospende da solo se la pagina ha errori (il riquadro degli errori compare solo al salvataggio manuale) e si ferma dopo un conflitto. Lato server il salvataggio automatico crea una revisione marcata `SalvataggioAutomatico`; **una revisione automatica in cima alla cronologia, non pubblicata e dello stesso autore, è sostituita dal salvataggio successivo (automatico o manuale)**, così la cronologia non accumula salvataggi intermedi. Le revisioni manuali e quelle pubblicate non si toccano mai; i numeri di revisione non si riusano (possibili salti). Il token di concorrenza resta obbligatorio.
- **Anteprima per dispositivo.** Pulsanti Desktop / Tablet / Mobile cambiano la larghezza del canvas (768 e 375 px); le regole responsive sono quelle del sito, caricate nel canvas.
- **Modifica del testo nel canvas.** Doppio clic su titolo, testo e pulsante: testo semplice (si legge `textContent`, mai HTML; incolla solo testo; lunghezza massima del catalogo; a riga singola gli a capo diventano spazi); Esc annulla, un clic fuori conferma. Nei nodi bloccati si modifica solo la proprietà ammessa dal template. Le scorciatoie che agiscono sui componenti (eliminare, spostarsi, copiare) sono disattivate perché scrivendo non si cancelli il componente selezionato; restano annulla e ripeti.
- **Testo formattato (rich text) senza sanitizzazione HTML.** Il componente `contenuto.testo-ricco` non contiene HTML: contiene un **markup a grammatica chiusa** (`TestoRicco`, `DvCms.Application.Builder`): paragrafi separati da una riga vuota, a capo, elenchi puntati (`- voce`), `**grassetto**`, `*corsivo*`, `[etichetta](indirizzo)` con indirizzo ammesso (`/percorso` o `https://`, senza spazi né parentesi) ed escape `\\ \* \[ \] \-`. Il server analizza il markup (limiti: 5000 caratteri, 60 blocchi, 200 voci, 50 collegamenti; marcatori da chiudere nello stesso blocco), lo riscrive in forma **canonica** (idempotente) e rifiuta ciò che non rientra nella grammatica; il renderer ricostruisce gli elementi da un insieme fisso (`p`, `br`, `strong`, `em`, `a rel="noopener"`, `ul`, `li`) con ogni valore codificato. Non esiste quindi HTML da ripulire e non serve alcuna libreria di sanitizzazione (nessuna nuova dipendenza). Nell'editor il testo si scrive nel pannello con una barra (grassetto, corsivo, collegamento, elenco) e il canvas mostra l'anteprima con la stessa grammatica (`src/testoRicco.js`); le due implementazioni sono verificate con gli **stessi 44 vettori di prova** (`frontend/page-builder/test/vettori-testo-ricco.json`, usati dai test .NET e da `npm test` in CI) e con un test di round-trip casuale lato server. Limiti noti: nessun annidamento oltre grassetto/corsivo, etichette dei collegamenti in testo semplice, nessun titolo o immagine dentro il testo formattato (sono componenti a sé), modifica solo nel pannello (il doppio clic nel canvas vi porta il cursore). Template `pagina-con-intestazione-fissa` v3 lo ammette nel corpo.

- **Griglia di caratteristiche e testimonianze (M5.5).** `contenuto.caratteristiche` (stile `colonne` 2 o 3, animazione) con 1–12 `contenuto.caratteristica` (titolo obbligatorio, descrizione facoltativa) e `contenuto.testimonianze` con 1–6 `contenuto.testimonianza` (citazione e autore obbligatori, ruolo facoltativo). Sono ammesse dentro una sezione (non nelle colonne, troppo strette). Nell'editor gli elementi figli nascono dentro il contenitore e non sono blocchi a sé; il renderer emette testo codificato ed elementi fissi (`h3`, `p`, `figure`, `blockquote`, `figcaption`), con gli stili solo tramite i token del contratto componenti-temi (nessun token nuovo). Il template `pagina-con-intestazione-fissa` v4 li ammette nel corpo; le versioni precedenti restano immutabili e una pagina passa alla v4 solo se il suo documento è valido. Un test (`ModelliVistaTest`) impedisce che una versione di modello rilasciata resti senza vista di rendering (senza, il sito ripiegherebbe in silenzio sul modello semplice).

- **Pannello delle proprietà (M5.8).** Le proprietà sono raggruppate in Contenuto, Aspetto e Animazione; i campi obbligatori (dal catalogo) sono segnati e dichiarati con `aria-required`; i campi di testo mostrano il contatore dei caratteri rispetto al massimo del catalogo e un campo obbligatorio vuoto è segnalato subito (`aria-invalid` e messaggio collegato). È solo guida: il modello del documento non cambia e il server riconfronta tutto al salvataggio (`frontend/page-builder/src/proprieta.js`).
- **Griglia di prodotti del negozio (M7.13).** `negozio.griglia-prodotti` (proprietà `quanti` 3/6/9/12, stili `colonne` 2 o 3 e animazione) è foglia, ammessa dentro una sezione (non nelle colonne) e dal template `pagina-con-intestazione-fissa` v5 nel corpo; le versioni precedenti restano immutabili. Il documento **non copia il catalogo** e non contiene identificativi di prodotto né prezzi: il renderer legge i prodotti pubblicati al momento della visita (stesso servizio della vetrina: solo pubblicati con una variante attiva, solo se il negozio è attivo, solo del tenant dell'host) e non produce nulla se non c'è niente da mostrare. Nome codificato, prezzo «da …» e «Esaurito» come in `/negozio`; link alla scheda. Nell'editor è un segnaposto tratteggiato («Griglia di prodotti · N»), chiaramente distinto dai dati reali. 
- **Scheda prodotto (M7.14).** `negozio.scheda-prodotto` ha una sola proprietà obbligatoria, `idProdotto` (tipo `IdProdotto`, un GUID), scelta nell'editor da un elenco dei prodotti non archiviati del sito (l'elenco è vuoto, e il blocco assente, se il negozio non è attivo). Il server rifiuta il salvataggio (`ProdottoNonDisponibile`) se il prodotto non esiste nel sito dell'host o è archiviato; una bozza è ammessa (si prepara la pagina prima di pubblicare) ma non si vede. Il renderer legge il prodotto alla visita con lo stesso servizio della vetrina: nome, descrizione, varianti con prezzo e «Esaurito», e un pulsante verso `/negozio/{id}`; un prodotto non più pubblicato, o un negozio spento, non produce nulla. Template e modello `pagina-con-intestazione-fissa` v6. **Scelta:** `negozio.dettaglio-prodotto` e `negozio.aggiungi-al-carrello` non sono componenti a sé: il modulo di acquisto vive solo in `/negozio/{id}`, dove il token antiforgery è legato all'acquirente; in una pagina del builder (contenuto condivisibile e memorizzabile in cache a livello di dati) un modulo di acquisto riaprirebbe quel problema per poco vantaggio, quindi la scheda rimanda alla pagina del prodotto.

- **Griglia di una categoria (M7.20).** `negozio.griglia-categoria` (proprietà obbligatoria `idCategoria`, tipo `IdCategoria`, scelta nell'editor fra le categorie del sito; `quanti` 3/6/9/12; stili `colonne` e animazione) mostra i prodotti **visibili** di una categoria, letti alla visita come per la griglia generale. Il server rifiuta il salvataggio (`CategoriaNonDisponibile`) se la categoria non esiste nel sito dell'host; una categoria poi svuotata o eliminata non produce nulla. Template e modello `pagina-con-intestazione-fissa` v7.

- **Percorso utente completo.** `smoke/percorso-utente.mjs` esegue con un browser vero, solo dall'interfaccia, il template guidato (titolo dell'intestazione, caratteristiche e pulsante nello slot del corpo, parti bloccate intatte) e la pagina libera (hero, testimonianze, fisarmonica), fino a salvataggio, pubblicazione e lettura da un visitatore anonimo.

## Accessibilità da tastiera dell'editor (M5)

Il canvas di GrapesJS è un iframe pensato per il mouse e resta tale. Tutto ciò che serve a costruire e modificare una pagina ha un equivalente fuori dal canvas, con controlli nativi o schemi WAI-ARIA noti (`frontend/page-builder/src/tastiera.js`):

- **Collegamenti rapidi** (visibili al focus) ai blocchi, alla struttura e alle proprietà; area `aria-live` che annuncia selezione, spostamento, duplicazione ed eliminazione.
- **Blocchi** come pulsanti: Invio o Spazio inserisce il blocco nel componente selezionato (se i vincoli lo ammettono), altrimenti dopo di esso, altrimenti nella pagina (`appendOnClick` di GrapesJS: stesse regole `droppable` del trascinamento).
- **Struttura:** albero `role=tree` dei componenti (frecce, Home, Fine, destra/sinistra per espandere e ridurre, Invio per passare ai campi delle proprietà, Esc per deselezionare); la selezione segue il focus; i nodi bloccati sono dichiarati «bloccato». Sostituisce il pannello Livelli di GrapesJS, che resta montato fuori pagina perché il trascinamento lo richiede.
- **Azioni** sul selezionato: sposta su/giù (solo fra fratelli, mai oltre un nodo del template), duplica, elimina; disabilitate quando il template o il catalogo lo vietano. Annulla/Ripeti nella barra.
- **Proprietà:** i campi di GrapesJS ricevono come nome accessibile l'etichetta della proprietà; la modifica del testo in linea (doppio clic) resta solo da mouse perché ogni testo è modificabile anche dal pannello.
- Scorciatoie che cancellano o spostano componenti restano disattivate; resta Ctrl+S.

Verifica: `frontend/page-builder/smoke/tastiera.mjs` percorre da sola tastiera un'intera costruzione di pagina (libera e con nodi bloccati) in un browser vero. **Non coperto:** prova con uno screen reader reale e audit automatico WCAG (axe o simili, nuova dipendenza da valutare con la regola 11), contrasto dei colori dell'interfaccia dell'editor, spostamento da tastiera di un componente in un altro contenitore (oggi: duplica/inserisci nel contenitore e riordina; non si trasferisce un componente esistente), tabelle e drag-and-drop dall'esterno. La modalità guidata a form resta l'alternativa piena.


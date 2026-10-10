# Valutazione del proof of concept del page builder (M4)

- **Data:** 2026-10-10
- **Riferimenti:** roadmap M4, backlog E-001…E-005, [ADR-0005](../decisions/ADR-0005-canonical-content-and-revisions.md), [ADR-0007](../decisions/ADR-0007-grapesjs-page-builder.md), [`page-builder.md`](../architecture/page-builder.md)
- **Esito:** **conversione affidabile per il catalogo di base: si può procedere con M5**, con le precisazioni e i rischi elencati sotto. Gli ADR sono confermati (stato *Accepted*) con le modifiche documentate in ciascuno.

La roadmap chiede di valutare il builder "con criteri espliciti" e di aggiornare gli ADR prima di estendere il catalogo se la conversione non fosse affidabile. I criteri sono fissati qui, ciascuno con la prova che lo verifica.

## Cosa è stato costruito

Sette componenti di base (`layout.sezione`, `layout.colonne`, `layout.colonna`, `contenuto.titolo`, `contenuto.testo`, `contenuto.immagine`, `contenuto.pulsante`), due template (`pagina-libera`: solo sezioni in radice; `pagina-con-intestazione-fissa`: intestazione e avviso bloccati, corpo come slot), due temi Razor già esistenti, l'editor GrapesJS in `/admin/pagine/{id}/builder`, il salvataggio con revisioni immutabili e il rendering pubblico. Il PoC obbligatorio di `page-builder.md` (sette punti) è coperto per intero.

## Criteri e risultati

| # | Criterio | Soglia | Risultato | Prova |
|---|---|---|---|---|
| 1 | **Montaggio isolato** | Editor in una Razor Page dedicata, asset dalla stessa origine, nessun backend Node, CSP invariata | **Superato.** Pagina senza script in linea; bundle di 1,15 MB (304 KB compressi) caricato solo da quella pagina; CSP `script-src 'self'` senza `unsafe-eval`, nessuna violazione osservata | `BuilderWebTest`, `smoke/percorso-completo.mjs` (Chromium, ascolta `securitypolicyviolation`) |
| 2 | **Persistenza e ricarica** | Il progetto salvato da un endpoint .NET si riapre identico | **Superato.** Salvataggio → ricarica restituisce gli stessi nodi e valori; revisione immutabile per ogni modifica; ripristino e modifica dei metadati portano con sé il documento | `BuilderWebTest`, smoke |
| 3 | **Fedeltà della conversione** | Round-trip deterministico per tutti i componenti; nulla perso in silenzio | **Superato per il catalogo di base.** documento → progetto → documento è identico e idempotente (stringa canonica); gli ID mancanti sono derivati dal percorso in modo deterministico; i valori di default espliciti non cambiano il documento. Ogni cosa non rappresentabile (tipi, classi, stili in linea, regole CSS, attributi, più pagine, risorse) è un **errore esplicito**, mai uno scarto | `TemplateEAdapterTest` (58 casi), `DocumentoCmsTest` (54 casi) |
| 4 | **Policy dei template lato server** | Nodo bloccato non modificabile né eliminabile con payload manipolato; slot solo con tipi ammessi | **Superato.** 15 manomissioni verificate sul validatore e 12 via API: rimozione, spostamento, modifica di tipo/proprietà/stili di nodi bloccati, nodi extra in radice o in nodi bloccati, tipi non ammessi nello slot, troppi figli. Il server ignora ogni informazione di blocco inviata dal browser | `TemplateEAdapterTest`, `BuilderWebTest.UnPayloadManomessoViaApiEDifesoDalServer`, smoke (rimozione di un nodo bloccato aggirando l'interfaccia) |
| 5 | **XSS** | Nessun JavaScript né HTML del tenant arriva al visitatore o all'admin | **Superato.** Il documento contiene solo testo, enumerazioni, GUID e indirizzi (`/percorso` o `https://`); il renderer codifica ogni valore e ricontrolla indirizzi e token in uscita; un documento rovinato nel database non produce HTML pericoloso; il progetto salvato è **rigenerato** dal documento, quindi il browser non può farvi memorizzare nulla che il documento non contenga | `DocumentoCmsTest`, `BuilderWebTest` (testo con `<script>`, URL `javascript:`, documento rovinato), smoke |
| 6 | **Concorrenza** | Nessuna sovrascrittura silenziosa | **Superato.** Token di concorrenza della pagina a ogni salvataggio; un token vecchio dà 409 e non scrive; un salvataggio senza modifiche non crea revisioni | `BuilderWebTest` |
| 7 | **Isolamento tra tenant e permessi** | Solo gli amministratori del tenant; nessun accesso incrociato | **Superato.** 404 per pagine di altri tenant (apertura, stato e salvataggio), reindirizzamento per anonimi, antiforgery obbligatorio, risorse media di altri tenant o private rifiutate | `BuilderWebTest` |
| 8 | **Rendering con più temi** | Stesso documento, due temi, contenuto non duplicato | **Superato.** Il contenuto del documento è identico con `classico` e `moderno`; cambia solo il contenitore del tema | `BuilderWebTest` |
| 9 | **Licenza e dipendenze** | Licenza compatibile con un prodotto commerciale, nessuna vulnerabilità nota, lockfile | **Superato con una segnalazione.** GrapesJS 0.23.6 BSD-3-Clause; dipendenze transitive tutte MIT; `npm audit` senza vulnerabilità; lockfile, bundle versionato e verifica in CI. Segnalazione: `backbone-undo` risulta non più supportato (nessuna vulnerabilità) | `frontend/page-builder/README.md`, CI |
| 10 | **Manutenibilità** | Nessun elenco di componenti duplicato tra server e client | **Superato.** Il catalogo che configura l'editor è generato dal registro .NET (tipi, proprietà, valori ammessi, figli); un test fissa l'impronta delle definizioni | `DocumentoCmsTest` |

## Cosa il PoC ha insegnato (scoperte, non errori)

1. **Il progetto dell'editor non si salva così com'è.** Contiene metadati dell'editor (nomi dei livelli, flag di interazione, `stylable`, `head`, `docEl` del contenitore) e, in generale, tutto ciò che il browser vuole. Si è scelto di **rigenerarlo dal documento validato**: con il catalogo di base (stili come token, nessun CSS libero) il documento contiene tutto ciò che serve per ricostruire il canvas, quindi non c'è stato dell'editor da conservare. Le colonne `ProgettoEditorJson` e `DocumentoRenderJson` restano distinte come da ADR-0005, ma la prima è una *proiezione* della seconda. Questo è anche ciò che rende sicura la riapertura nell'admin.
2. **L'adapter ha dovuto imparare i metadati del contenitore** scritti da GrapesJS 0.22+: sono accettati solo nella forma nota, qualunque altra forma è un errore.
3. **Le regole sul contenitore principale vanno applicate dopo il caricamento**, perché il progetto caricato lo ricrea (la prima versione non le applicava e l'interfaccia permetteva di inserire sezioni fuori dallo slot; il server le avrebbe rifiutate comunque).
4. **GrapesJS carica Font Awesome da un CDN** per impostazione predefinita: la CSP lo bloccava. Disattivato (`cssIcons`) e sostituito da caratteri semplici.
5. **I trait scrivono i valori di default come attributi espliciti**: il server normalizza (toglie default e vuoti) prima di confrontare col template; un test garantisce che i template siano già in forma canonica.
6. **Il bundle contiene `new Function`** (templating di underscore). Nei percorsi usati non viene eseguito e la CSP lo bloccherebbe comunque; il test di fumo lo controlla a ogni aggiornamento.
7. **La drag-and-drop funziona con il mouse reale** in Chromium (anche in test automatico); il test di fumo la usa.

## Limiti del PoC e rischi per M5

- **Accessibilità dell'editor.** GrapesJS è pensato per il mouse: la riorganizzazione da tastiera è limitata. Rischio da valutare in M5 (requisiti WCAG per l'admin); l'alternativa accessibile resta la modalità guidata a form.
- **Rich text e sanitizzazione.** Il PoC ha solo testo semplice. Il rich text richiede una libreria di sanitizzazione (nuova dipendenza, da valutare con la regola 11) e una policy testata prima di entrare nel catalogo.
- **Modifica del testo direttamente nel canvas, anteprime per dispositivo, autosave con debounce, pannello proprietà avanzato, animazioni (E-006):** non fanno parte del PoC. Il server è pronto per l'autosave (token, salvataggi senza modifica che non creano revisioni).
- **Contratto tra componenti e temi.** I temi già rilasciati sono immutabili: gli stili dei componenti stanno in un foglio a parte (`componenti.css`) con poche regole per tema. Serve un contratto esplicito prima di molti componenti.
- **Versioni dei componenti e dei template.** Esiste solo la versione 1: la migrazione di documenti tra versioni di template o di schema non è ancora definita (il passaggio a una nuova versione è rifiutato per le pagine del builder).
- **Immagini.** Il testo alternativo è una proprietà del nodo, obbligatoria salvo immagine decorativa; il collegamento con i testi alternativi per lingua della libreria media (ADR-0018) è da fare.
- **`backbone-undo`** non più supportato: da rivalutare se GrapesJS non lo sostituisce.
- **Catalogo e-commerce** (`negozio.*`) non toccato: dipende dal modulo commerce.

## Decisione

Si prosegue verso M5 estendendo il catalogo a partire dai criteri sopra, che restano la base di verifica: ogni nuovo componente aggiunge casi di round-trip, di manomissione e di rendering, e l'impronta del registro viene aggiornata consapevolmente.

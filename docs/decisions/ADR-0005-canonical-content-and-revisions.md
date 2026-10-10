# ADR-0005 — Documento CMS canonico e revisioni immutabili

- **Stato:** Accepted (confermato dal proof of concept M4, 2026-10-10; vedi "Esito del proof of concept")
- **Data:** 2026-10-09
- **Aggiornato:** 2026-10-10 (esito del PoC: il progetto dell'editor è una proiezione rigenerata dal documento)

## Contesto

Le pagine si modificano con form guidati o GrapesJS, si renderizzano con temi Razor e devono restare migrabili. Il JSON interno dell'editor è necessario a riaprire il canvas; HTML esportato perde alcune informazioni e non è ideale come modello di dominio.

## Decisione proposta

Persistire `ProgettoEditorJson` e `DocumentoRenderJson` distinti. Il documento CMS è un albero tipizzato/versionato, renderizzato solo tramite registry allow-list. Le revisioni sono immutabili, con puntatori separati per draft corrente e pubblicato. Pubblicare cambia il puntatore in transazione.

## Alternative considerate

- HTML/CSS esportato come fonte di verità: più semplice inizialmente, ma debole per tipizzazione, componenti dinamici, migrazioni, template lock e compatibilità tema.
- Solo JSON GrapesJS come modello dominio: accoppiamento eccessivo all'editor e renderer server-side meno controllabile.

## Conseguenze

Serve un adapter e uno schema CMS, con costo upfront. Prima di estendere il builder, lo spike deve dimostrare conversione affidabile per il set iniziale di componenti. Se non è possibile senza perdite inaccettabili, aggiornare questo ADR prima di continuare.

## Esito del proof of concept (M4, 2026-10-10)

Lo spike ha dimostrato che la conversione è affidabile per il catalogo di base (valutazione completa in [`docs/delivery/valutazione-poc-m4.md`](../delivery/valutazione-poc-m4.md)). La decisione resta, con queste precisazioni:

- **`DocumentoRenderJson`** è il documento canonico: JSON versionato (`versioneSchema`, `nodi` con `id`, `tipo`, `proprieta`, `stili`, `figli`), scritto in forma canonica (chiavi in ordine, proprietà uguali al default omesse) e letto in modo rigido (campi sconosciuti e chiavi duplicate sono errori). È l'unica cosa che il renderer disegna, tramite un registro di componenti in allow-list con impronta fissata da un test.
- **`ProgettoEditorJson`** resta una colonna distinta, ma è una **proiezione rigenerata dal server** a partire dal documento già validato, non il JSON inviato dal browser. Con il catalogo di base (stili come token, nessun CSS libero) il documento contiene tutto ciò che serve a riaprire il canvas, quindi non si perde stato dell'editor; in cambio il browser non può far memorizzare nel progetto nulla che il documento non contenga (rilevante perché il progetto viene ricaricato nell'origine dell'admin). Se componenti futuri avranno stato dell'editor non derivabile dal documento (ad esempio CSS per breakpoint), si riesaminerà questo punto con un nuovo ADR.
- **Adapter** deterministico e senza perdite silenziose: ciò che il documento non rappresenta è un errore esplicito.
- **Revisioni:** nuove colonne nullable su `RevisioniPagina`; ogni salvataggio che cambia il documento crea una nuova revisione immutabile; un salvataggio senza modifiche non ne crea. Il passaggio a una nuova versione di template per le pagine del builder non è ancora previsto.
- **Template e policy:** versioni immutabili (impronta fissata da un test), con nodi bloccati e slot; la policy è confrontata dal server a ogni salvataggio. L'id del template coincide con l'id del modello di pagina.

## Salvataggi automatici (M5, 2026-10-10)

Le revisioni restano immutabili: nessuna si modifica dopo la creazione. Per non riempire la cronologia con i salvataggi intermedi dell'editor, una revisione marcata `SalvataggioAutomatico` che sia **in cima alla cronologia, non pubblicata e dello stesso autore** viene *rimossa* (mai modificata) quando arriva il salvataggio successivo, automatico o manuale, che crea una nuova revisione. Non vengono mai rimosse revisioni manuali né pubblicate, né una revisione automatica non più in cima o di un altro autore. I numeri di revisione non si riusano. Questa è l'unica eccezione alla conservazione di tutte le revisioni e riguarda solo bozze effimere che nessuno ha scelto di tenere.


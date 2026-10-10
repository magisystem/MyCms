# ADR-0007 — GrapesJS per l'editor visuale isolato

- **Stato:** Accepted (proof of concept M4 superato e licenza verificata, 2026-10-10)
- **Data:** 2026-10-09
- **Aggiornato:** 2026-10-10 (esito del PoC, vincoli di integrazione)

## Contesto

Un editor drag-and-drop completo richiede canvas, gestione selezione, proprietà, undo/redo, responsive preview e component registry. Costruirlo da zero sottrarrebbe molto tempo al CMS.

## Decisione proposta

Valutare GrapesJS come motore dell'editor visuale in una Razor Page dedicata. Usare un bundle JS isolato e API ASP.NET Core per load/store. Non introdurre una SPA generale o backend Node. Persistire il project JSON per ripristinare correttamente il canvas.

## Alternative considerate

- Costruire un editor da zero: rifiutato salvo fallimento del POC.
- Angular/React per l'intera admin: rifiutato per non correlato bisogno architetturale.
- Solo form guidati: non soddisfa la richiesta di libera composizione visuale.

## Conseguenze

Serve codice JavaScript e una toolchain di build limitata al builder. Va verificata licenza, sicurezza, manutenzione e qualità dell'adapter verso il documento CMS. Il POC deve essere superato prima di creare un ampio catalogo di componenti.

## Riferimento

[GrapesJS Storage Manager](https://grapesjs.com/docs/modules/Storage.html)

## Esito del proof of concept (M4, 2026-10-10)

GrapesJS 0.23.6 (BSD-3-Clause) funziona come motore dell'editor nelle condizioni dell'ADR; valutazione e prove in [`docs/delivery/valutazione-poc-m4.md`](../delivery/valutazione-poc-m4.md). Vincoli di integrazione confermati:

- **Isolamento:** bundle compilato con esbuild in `frontend/page-builder` (lockfile, versioni esatte), **versionato** in `wwwroot/lib/page-builder` perché in produzione non serve Node; la CI verifica che corrisponda a sorgenti e lockfile e che `npm audit` non abbia vulnerabilità da "high" in su. Caricato solo da `/admin/pagine/{id}/builder`.
- **CSP invariata:** `script-src 'self'` senza `unsafe-eval`, nessuna risorsa esterna (le icone predefinite da CDN sono disattivate). Il bundle contiene `new Function` (templating di underscore) ma nei percorsi usati non viene eseguito; un test di fumo con browser vero lo controlla a ogni aggiornamento.
- **Il browser non è un'autorità:** vincoli dei template e catalogo servono a guidare l'interfaccia; il server riconverte, valida e riconfronta col template a ogni salvataggio e rigenera il progetto (ADR-0005).
- **Superficie ridotta:** si montano solo blocchi, livelli e proprietà; non gli editor di codice, l'asset manager né il rich text. Nessuna regola CSS libera, nessuna classe: gli stili sono token del documento.
- **Dipendenze:** tutte MIT salvo GrapesJS; `backbone-undo` non più supportato (nessuna vulnerabilità), da rivalutare prima di M5.
- **Rischio aperto:** accessibilità da tastiera dell'editor; la modalità guidata a form resta l'alternativa accessibile.


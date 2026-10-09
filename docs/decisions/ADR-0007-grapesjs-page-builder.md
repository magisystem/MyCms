# ADR-0007 — GrapesJS per l'editor visuale isolato

- **Stato:** Proposed (gated da proof of concept e verifica licenza)
- **Data:** 2026-10-09

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

# ADR-0008 — Rendering pubblico server-side e publish immutabile

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Le pagine pubbliche devono essere SEO-friendly, funzionare senza JavaScript per il contenuto essenziale e avere un flusso di pubblicazione verificabile.

## Decisione

Renderizzare pagine pubbliche con Razor dal documento validato e dalla revisione pubblicata. Non usare il JavaScript del page builder nel frontend pubblico salvo asset/interazioni dichiarati del tema. La pubblicazione crea/approva una revisione immutabile e aggiorna un riferimento live atomico.

## Alternative considerate

- Rendering pubblico in SPA: non necessario per gli obiettivi SEO e contrario alla scelta Razor-first.
- Salvare soltanto HTML pre-renderizzato: non è il modello canonico; eventualmente valutabile come cache derivata in seguito.

## Conseguenze

Serve registry di renderer server-side e cache invalidation. Il contenuto principale è disponibile nel markup iniziale. Preview e draft devono avere accesso protetto e `noindex`.

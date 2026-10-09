# ADR-0005 — Documento CMS canonico e revisioni immutabili

- **Stato:** Proposed (approvazione finale dopo spike del builder)
- **Data:** 2026-10-09

## Contesto

Le pagine si modificano con form guidati o GrapesJS, si renderizzano con temi Razor e devono restare migrabili. Il JSON interno dell'editor è necessario a riaprire il canvas; HTML esportato perde alcune informazioni e non è ideale come modello di dominio.

## Decisione proposta

Persistire `ProgettoEditorJson` e `DocumentoRenderJson` distinti. Il documento CMS è un albero tipizzato/versionato, renderizzato solo tramite registry allow-list. Le revisioni sono immutabili, con puntatori separati per draft corrente e pubblicato. Pubblicare cambia il puntatore in transazione.

## Alternative considerate

- HTML/CSS esportato come fonte di verità: più semplice inizialmente, ma debole per tipizzazione, componenti dinamici, migrazioni, template lock e compatibilità tema.
- Solo JSON GrapesJS come modello dominio: accoppiamento eccessivo all'editor e renderer server-side meno controllabile.

## Conseguenze

Serve un adapter e uno schema CMS, con costo upfront. Prima di estendere il builder, lo spike deve dimostrare conversione affidabile per il set iniziale di componenti. Se non è possibile senza perdite inaccettabili, aggiornare questo ADR prima di continuare.

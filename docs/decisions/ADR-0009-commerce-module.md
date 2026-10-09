# ADR-0009 — E-commerce come modulo di dominio interno

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Alcuni tenant devono vendere prodotti; le pagine possono mostrare catalogo e CTA commerciali, ma il CMS non deve diventare la fonte di verità di prezzi, disponibilità o pagamenti.

## Decisione

Creare un modulo Store/Commerce interno con servizi, entità e transizioni proprie. Il builder usa componenti che referenziano prodotti/collezioni. Checkout e totali sono verificati lato server; gli ordini conservano snapshot; provider e webhook sono incapsulati tramite contratti.

## Conseguenze

Commerce si sviluppa dopo il core CMS. Provider, tassazione, valute, spedizioni e politica stock restano decisioni aperte, da finalizzare prima del checkout di produzione.

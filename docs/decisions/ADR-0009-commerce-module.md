# ADR-0009 — E-commerce come modulo di dominio interno

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Alcuni tenant devono vendere prodotti; le pagine possono mostrare catalogo e CTA commerciali, ma il CMS non deve diventare la fonte di verità di prezzi, disponibilità o pagamenti.

## Decisione

Creare un modulo Store/Commerce interno con servizi, entità e transizioni proprie. Il builder usa componenti che referenziano prodotti/collezioni. Checkout e totali sono verificati lato server; gli ordini conservano snapshot; provider e webhook sono incapsulati tramite contratti.

## Account e ordini

Gli ordini richiedono un account autenticato (`Utente`, `TenantAdmin`, `PlatformAdmin`); il `Guest` non può ordinare (ADR-0011).

## Conseguenze

Commerce si sviluppa dopo il core CMS. Provider, tassazione, valute, spedizioni e politica stock restano decisioni aperte, da finalizzare prima del checkout di produzione.

Aggiornamento 2026-10-11: provider (PayPal), valuta (EUR), IVA, spedizioni e prenotazione della giacenza sono stati decisi in [ADR-0020](ADR-0020-commerce-policies-and-paypal.md); restano aperti resi e rimborsi e i dati personali degli acquirenti.

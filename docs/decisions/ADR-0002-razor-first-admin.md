# ADR-0002 — Razor-first, senza SPA generale

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Il prodotto è principalmente .NET/C#; il sito pubblico richiede HTML server-side per SEO. La maggior parte dell'admin è CRUD e workflow classico. Il page builder, invece, necessita di un canvas browser altamente interattivo.

## Decisione

Usare Razor Pages/MVC per sito pubblico e amministrazione. L'interfaccia admin usa HTML e form tradizionali con Post/Redirect/Get. HTMX o JavaScript sono aggiunti solo dove risolvono un bisogno specifico. GrapesJS viene caricato in una pagina Razor dedicata. Non introdurre React, Angular, Blazor WebAssembly o backend Node.js senza nuovo ADR.

## Alternative considerate

- SPA Angular/React per tutta l'admin: rifiutata come complessità non necessaria in questa fase.
- Blazor per tutto: non scelto perché il builder esistente è JavaScript e l'admin standard non richiede SPA.
- HTMX obbligatorio: rifiutato; è un'opzione, non una base architetturale.

## Conseguenze

Meno codebase frontend e minore duplicazione del rendering. Alcune schermate fanno round trip HTTP, comportamento accettato. Il builder conserva un modulo JS autonomo, ma API e backend rimangono ASP.NET Core.

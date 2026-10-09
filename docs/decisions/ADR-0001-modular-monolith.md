# ADR-0001 — Monolite modulare

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Il CMS deve offrire tenancy, pagine, temi, media, SEO e commerce, ma il team e il prodotto sono nella fase iniziale. Microservizi anticiperebbero deployment, osservabilità, comunicazione distribuita e gestione dati senza un vantaggio attualmente dimostrato.

## Decisione

Implementare un monolite modulare ASP.NET Core. Separare logicamente i moduli; usare progetti/assembly multipli quando aiutano i confini, ma non creare un servizio per ogni modulo. Worker separato ammesso per operazioni asincrone senza trasformare il dominio in microservizi.

## Alternative considerate

- Microservizi dal giorno zero: rifiutati per costi e complessità prematuri.
- Applicazione monolitica senza confini di modulo: rifiutata perché facilita accoppiamenti eccessivi.

## Conseguenze

Deployment iniziale semplice. I moduli condividono processo e possono condividere il DB, ma non devono chiamare direttamente le implementazioni interne degli altri moduli al di fuori dei contratti stabiliti. Un eventuale split futuro richiederà una decisione basata su carico, ownership o isolamento.

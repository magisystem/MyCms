# ADR-0010 — Outbox transazionale e worker

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Pubblicazione, ordini, pagamenti ed email generano effetti esterni. Se l'applicazione salva nel DB ma fallisce prima di notificare un worker, l'effetto potrebbe perdersi.

## Decisione

Per eventi importanti, salvare un messaggio outbox nella stessa transazione della modifica di dominio. Un worker legge e processa i messaggi con retry e idempotenza. Partire con tecnologia semplice; broker esterno solo se carico e requisiti lo giustificano.

## Conseguenze

Servono stato, tentativi, gestione errori e monitoring del worker. Gli handler devono tollerare consegna ripetuta.

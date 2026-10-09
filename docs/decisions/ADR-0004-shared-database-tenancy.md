# ADR-0004 — Database condiviso con isolamento logico tenant

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Il SaaS gestirà molti tenant e più siti per tenant. Database separati per cliente aumenterebbero costi, provisioning e migrazioni prima che siano noti requisiti specifici.

## Decisione

Usare inizialmente un database condiviso e tabelle condivise con `TenantId`/`SiteId`, vincoli, query filter e autorizzazioni server-side. Valutare Row-Level Security nativa di SQL Server o PostgreSQL come difesa aggiuntiva, con implementazioni e test provider-specifici. Ogni nuovo modulo tenant-scoped deve avere test cross-tenant.

## Alternative considerate

- Database per tenant: non predefinito; utile eventualmente per clienti con requisiti di isolamento dedicati.
- Schema separato per tenant: scartato come modello iniziale per costi di gestione e migrazione.

## Conseguenze

Le query e le relazioni richiedono disciplina. ID casuali non sostituiscono autorizzazione. Export, media, cache e job sono inclusi nel threat model. Una futura tenancy dedicata richiederà migrazione progettata.

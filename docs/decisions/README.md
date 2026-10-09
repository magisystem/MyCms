# Architecture Decision Records (ADR)

Gli ADR registrano decisioni che influenzano struttura, dipendenze o costi futuri. Gli ADR accettati non si riscrivono silenziosamente: una scelta nuova crea un ADR che supersede la decisione precedente.

Stato: `Proposed`, `Accepted`, `Superseded`, `Deprecated`.

## Indice

- [ADR-0001 — Monolite modulare](ADR-0001-modular-monolith.md)
- [ADR-0002 — Razor-first, no SPA generale](ADR-0002-razor-first-admin.md)
- [ADR-0003 — .NET 10, EF Core 10, SQL Server (superseded in parte da ADR-0012)](ADR-0003-dotnet-sql-server-postgresql.md)
- [ADR-0004 — Database condiviso e isolamento tenant](ADR-0004-shared-database-tenancy.md)
- [ADR-0005 — Contenuto canonico e revisioni](ADR-0005-canonical-content-and-revisions.md)
- [ADR-0006 — Temi Razor fidati e versionati](ADR-0006-trusted-razor-themes.md)
- [ADR-0007 — GrapesJS isolato nell'editor](ADR-0007-grapesjs-page-builder.md)
- [ADR-0008 — Rendering server-side e pubblicazione](ADR-0008-server-rendering-and-publishing.md)
- [ADR-0009 — E-commerce come modulo interno](ADR-0009-commerce-module.md)
- [ADR-0010 — Outbox e worker](ADR-0010-outbox-worker.md)
- [ADR-0011 — Identity e ruoli](ADR-0011-identity-and-membership.md)
- [ADR-0012 — SQL Server come unico database](ADR-0012-sql-server-only.md)
- [ADR-0013 — Tabler e Tabler Icons per l'admin](ADR-0013-tabler-admin-ui.md)
- [ADR-0014 — Amministrazione di piattaforma, domini e certificati manuali](ADR-0014-platform-administration.md)
- [ADR-0015 — Invio email tramite adapter (Graph, Gmail, SendGrid, SMTP)](ADR-0015-email-delivery.md)

Per le regole operative della persistenza, vedere [`docs/architecture/database.md`](../architecture/database.md).

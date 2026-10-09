# Architecture Decision Records (ADR)

Gli ADR registrano decisioni che influenzano struttura, dipendenze o costi futuri. Gli ADR accettati non si riscrivono silenziosamente: una scelta nuova crea un ADR che supersede la decisione precedente.

Stato: `Proposed`, `Accepted`, `Superseded`, `Deprecated`.

## Indice

- [ADR-0001 — Monolite modulare](ADR-0001-modular-monolith.md)
- [ADR-0002 — Razor-first, no SPA generale](ADR-0002-razor-first-admin.md)
- [ADR-0003 — .NET 10, EF Core 10, SQL Server primario e PostgreSQL supportato](ADR-0003-dotnet-sql-server-postgresql.md)
- [ADR-0004 — Database condiviso e isolamento tenant](ADR-0004-shared-database-tenancy.md)
- [ADR-0005 — Contenuto canonico e revisioni](ADR-0005-canonical-content-and-revisions.md)
- [ADR-0006 — Temi Razor fidati e versionati](ADR-0006-trusted-razor-themes.md)
- [ADR-0007 — GrapesJS isolato nell'editor](ADR-0007-grapesjs-page-builder.md)
- [ADR-0008 — Rendering server-side e pubblicazione](ADR-0008-server-rendering-and-publishing.md)
- [ADR-0009 — E-commerce come modulo interno](ADR-0009-commerce-module.md)
- [ADR-0010 — Outbox e worker](ADR-0010-outbox-worker.md)
- [ADR-0011 — Identity e membership tenant](ADR-0011-identity-and-membership.md)

Per le regole operative della persistenza multi-provider, vedere [`docs/architecture/database-providers.md`](../architecture/database-providers.md).

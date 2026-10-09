# ADR-0003 — .NET 10, EF Core 10, SQL Server primario e PostgreSQL supportato

- **Stato:** Accepted
- **Data:** 2026-10-09
- **Nota:** questa decisione aggiorna la baseline iniziale PostgreSQL-first prima del bootstrap applicativo.

## Contesto

Il CMS SaaS richiede un runtime supportato, un ORM maturo e un database relazionale con vincoli, transazioni e strumenti operativi robusti. La preferenza del progetto è Microsoft SQL Server. È inoltre richiesto supporto per PostgreSQL, quindi la portabilità va trattata come requisito fin dall'inizio e verificata continuamente.

## Decisione

- Usare .NET 10 LTS, ASP.NET Core 10 ed EF Core 10, con patch supportate e versioni di provider compatibili.
- Usare Microsoft SQL Server come provider primario/predefinito per sviluppo, CI principale ed esempi di deployment.
- Supportare PostgreSQL come secondo provider tramite configurazione esplicita, migrazioni separate e test automatici reali.
- Mantenere due serie distinte di migrazioni e relativi model snapshot. Ogni cambiamento di schema deve aggiornare e testare entrambe, salvo deroga documentata.
- Tenere le differenze di provider in Infrastructure e nelle migration; Domain e Application non devono dipendere dal provider specifico.
- Evitare funzionalità specifiche di un database nel modello condiviso senza un ADR che descriva la compatibilità.

## Alternative considerate

- **SQL Server soltanto:** più semplice; non soddisfa il requisito esplicito di supportare anche PostgreSQL.
- **PostgreSQL primario:** tecnicamente valido, ma non corrisponde alla preferenza primaria del progetto.
- **Cambiare provider tramite sola connection string e condividere una migration unica:** respinto perché il SQL generato e le semantiche differiscono fra provider.
- **Due codebase applicative:** respinto; la logica di dominio deve essere comune, con differenze confinate alla persistenza.

## Conseguenze

- SQL Server è il default, ma la CI deve verificare anche PostgreSQL.
- Ogni modifica schema richiede due migration set e review, aumentando il costo manutentivo.
- Tipi, collazioni, JSON, indici, paging, transazioni e concorrenza devono essere testati su entrambi i motori.
- Un database non può essere trasferito da un provider all'altro cambiando configurazione: serve un progetto di migrazione dati separato.
- Prima del bootstrap, verificare la versione esatta di ciascun provider compatibile con EF Core 10 e fissarla con package versioning centralizzato.

## Riferimenti

- [Policy supporto .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [EF Core releases](https://learn.microsoft.com/en-us/ef/core/what-is-new/)
- [Provider SQL Server](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)
- [Migrations with Multiple Providers](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)

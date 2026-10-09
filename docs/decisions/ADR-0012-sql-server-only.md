# ADR-0012 — SQL Server come unico database supportato

- **Stato:** Accepted
- **Data:** 2026-10-09
- **Supera:** ADR-0003 (parte relativa al supporto PostgreSQL e alla doppia serie di migrazioni)

## Contesto

ADR-0003 richiedeva SQL Server primario e PostgreSQL come secondo provider, con due serie di migrazioni, CI a matrice e test su entrambi i motori. Il committente utilizza SQL Server di Microsoft e non ha un'esigenza concreta di PostgreSQL. Il supporto doppio aveva un costo elevato in migrazioni, test e vincoli sul modello dati, senza un beneficio per il prodotto.

## Decisione

- SQL Server è l'unico database supportato, tramite EF Core 10.
- Una sola serie di migrazioni EF Core.
- Restano validi di ADR-0003: .NET 10 LTS, ASP.NET Core 10, EF Core 10 con provider della stessa major e il divieto di dipendenze dal provider in Dominio e Applicazione.
- Non si aggiunge PostgreSQL (né un altro provider) senza un nuovo ADR.

## Alternative considerate

- **Mantenere il doppio provider:** respinto per costi di manutenzione e test senza utilità attuale.
- **PostgreSQL come unico database:** non corrisponde alla piattaforma già in uso.

## Conseguenze

- Si possono usare con cautela le funzionalità di SQL Server (`rowversion`, indici filtrati, hint di locking nel claim outbox, Row-Level Security), isolandole in Infrastruttura.
- Il modello e le migrazioni sono più semplici e la CI più veloce.
- Il passaggio a un altro motore richiederebbe lavoro e un ADR dedicato. Il costo è accettato.
- Le regole operative sono in [`docs/architecture/database.md`](../architecture/database.md).

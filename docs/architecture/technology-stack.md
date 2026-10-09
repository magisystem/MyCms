# Stack tecnologico

## Scelte iniziali

| Area | Scelta | Note |
|---|---|---|
| Runtime | .NET 10 LTS | Applicare le patch correnti supportate. |
| Web | ASP.NET Core 10 | Razor Pages e MVC per sito pubblico e admin. |
| ORM | EF Core 10 | Mantenere la stessa major version di EF Core e dei provider. |
| DB primario | Microsoft SQL Server | Provider predefinito per sviluppo, CI primaria e produzione salvo configurazione esplicita. |
| DB secondario | PostgreSQL | Provider supportato, con migrazioni distinte e test d'integrazione dedicati. Non presumere che una connection string basti a cambiare provider. |
| UI admin | Razor Pages + HTML/CSS | No framework SPA generale. HTMX è opzionale. |
| Editor | GrapesJS | Bundle dedicato, estensioni JavaScript; nessun backend Node.js. |
| File/media | Object storage via astrazione applicativa | File system solo per sviluppo locale. |
| Cache | Interfaccia cache; Redis quando si scala su più istanze | Non fare affidamento su cache in-memory per coerenza cross-instance. |
| Background jobs | Hosted Service/worker + outbox iniziale | Valutare libreria di job solo con requisiti espliciti. |
| Auth | ASP.NET Core Identity come default iniziale | SSO/OIDC resta estensione futura se richiesta. |
| Test | xUnit o NUnit, da selezionare prima del bootstrap | Non usare due framework test concorrenti senza ragione. |
| Logging | ILogger + logging strutturato | Provider APM/trace da scegliere con hosting. |

## Strategia database

La piattaforma deve supportare SQL Server e PostgreSQL, con **SQL Server come target primario**. SQL Server è il default per le configurazioni locali, la suite CI principale e gli esempi di deployment. PostgreSQL non è un'idea futura: è un provider aggiuntivo da mantenere verificato tramite una suite di compatibilità.

Le due piattaforme hanno differenze di SQL, tipi, collazioni, gestione JSON, indici e migrazioni. Per questo:

- usare EF Core, senza esporre API specifiche del provider nei moduli Domain/Application;
- mantenere una serie di migrazioni EF Core per SQL Server e una separata per PostgreSQL;
- per ogni modifica dello schema, aggiornare e verificare entrambe le serie nello stesso PR, salvo eccezione approvata e documentata;
- eseguire test d'integrazione su istanze reali di entrambi i database; il test primario usa SQL Server;
- evitare feature specifiche di un database nel modello condiviso, salvo mapping/migrazioni provider-specifici espliciti;
- non fare affidamento sul provider EF Core InMemory per validare vincoli, transazioni, query, concorrenza o migrazioni;
- documentare ogni differenza provider-specifica e testarla su entrambi i motori.

Per i dettagli operativi, incluse configurazione, migrazioni, tipi e CI, leggere [Strategia provider database](database-providers.md).

## Versioning

- Usare .NET 10 LTS e EF Core 10, aggiornati alle ultime patch supportate.
- Allineare la major version di ciascun provider con EF Core 10 e verificare la compatibilità ufficiale prima di fissare le versioni NuGet. Non presumere che i due provider vengano rilasciati nello stesso ciclo.
- Pinning SDK tramite `global.json` dopo il bootstrap, senza bloccare a lungo patch obsolete.
- Centralizzare versioni NuGet in `Directory.Packages.props` se la soluzione cresce oltre un progetto.
- Pinning delle dipendenze JS nel lockfile del page builder.
- Riesaminare dipendenze e licenze prima di ogni rilascio commerciale.

## Frontend

Le normali pagine Razor non richiedono una pipeline Node. La cartella del page builder può usare npm e un bundler (per esempio Vite) limitatamente alla build degli asset del builder. Il runtime di produzione è ASP.NET Core; non viene aggiunto un server Node.

La libreria CSS dell'admin resta una scelta da finalizzare durante il bootstrap. Preferire un sistema semplice e accessibile a un framework frontend aggiuntivo.

## Riferimenti ufficiali

- [Policy ufficiale di supporto .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [EF Core — release e pianificazione](https://learn.microsoft.com/en-us/ef/core/what-is-new/)
- [EF Core SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)
- [EF Core database providers](https://learn.microsoft.com/en-us/ef/core/providers/)
- [EF Core migrations with multiple providers](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)
- [Razor Pages](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/)
- [GrapesJS Storage Manager](https://grapesjs.com/docs/modules/Storage.html)

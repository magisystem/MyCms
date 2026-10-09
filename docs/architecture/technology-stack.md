# Stack tecnologico

## Scelte iniziali

| Area | Scelta | Note |
|---|---|---|
| Runtime | .NET 10 LTS | Applicare le patch correnti supportate. |
| Web | ASP.NET Core 10 | Razor Pages e MVC per sito pubblico e admin. |
| ORM | EF Core 10 | Mantenere la stessa major version di EF Core e dei provider. |
| Database | Microsoft SQL Server | Unico database supportato (ADR-0012). Una sola serie di migrazioni EF Core. |
| UI admin | Razor Pages + Tabler 1.6.1 (MIT) | Tema in `admin-theme/tabler`, `dist/` precompilato; cartella copiata per intero in `wwwroot/lib/tabler`. No framework SPA generale. HTMX è opzionale (ADR-0013). |
| Icone admin | Tabler Icons 3.49.0 (MIT) | `admin-theme/tabler-icons`, copiata per intero in `wwwroot/lib/tabler-icons`. |
| Editor | GrapesJS | Bundle dedicato, estensioni JavaScript; nessun backend Node.js. |
| File/media | Object storage via astrazione applicativa | File system solo per sviluppo locale. |
| Cache | Interfaccia cache; Redis quando si scala su più istanze | Non fare affidamento su cache in-memory per coerenza cross-instance. |
| Background jobs | Hosted Service/worker + outbox iniziale | Valutare libreria di job solo con requisiti espliciti. |
| Auth | Account locali propri (tabella `Utenze`), cookie authentication di ASP.NET Core, hash password BCrypt (`BCrypt.Net-Next`, da verificare); senza ASP.NET Core Identity (ADR-0016) | Ruoli `PlatformAdmin`, `TenantAdmin`, `Utente` (acquirente), `Guest` per tenant (ADR-0011); recupero password con OTP e link monouso; 2FA per ultima. SSO/OIDC resta estensione futura se richiesta. |
| Email | Adapter intercambiabili: Microsoft Graph API, Gmail API, SendGrid, SMTP | Contratto unico in Application, canale scelto da configurazione, invio dal worker via outbox (ADR-0015). |
| Test | xUnit o NUnit, da selezionare prima del bootstrap | Non usare due framework test concorrenti senza ragione. |
| Logging | ILogger + logging strutturato | Provider APM/trace da scegliere con hosting. |

## Strategia database

La piattaforma usa **Microsoft SQL Server** come unico database supportato (ADR-0012), con EF Core 10 e una sola serie di migrazioni. Le funzionalità specifiche di SQL Server si usano solo in Infrastructure, con test su un'istanza reale. Per i dettagli operativi leggere [Database SQL Server](database.md).

## Versioning

- Usare .NET 10 LTS e EF Core 10, aggiornati alle ultime patch supportate.
- Allineare la versione del provider SQL Server alla major di EF Core 10 e fissare le versioni NuGet.
- Pinning SDK tramite `global.json` dopo il bootstrap, senza bloccare a lungo patch obsolete.
- Centralizzare versioni NuGet in `Directory.Packages.props` se la soluzione cresce oltre un progetto.
- Pinning delle dipendenze JS nel lockfile del page builder.
- Riesaminare dipendenze e licenze prima di ogni rilascio commerciale.

## Frontend

Le normali pagine Razor non richiedono una pipeline Node. La cartella del page builder può usare npm e un bundler (per esempio Vite) limitatamente alla build degli asset del builder. Il runtime di produzione è ASP.NET Core; non viene aggiunto un server Node.

L'admin usa Tabler (CSS/JS precompilati) e Tabler Icons da `admin-theme/` (ADR-0013). Al bootstrap si copiano in `wwwroot` solo gli asset usati; nessuna pipeline Node per l'admin.

## Riferimenti ufficiali

- [Policy ufficiale di supporto .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [EF Core — release e pianificazione](https://learn.microsoft.com/en-us/ef/core/what-is-new/)
- [EF Core SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)
- [EF Core database providers](https://learn.microsoft.com/en-us/ef/core/providers/)
- [Razor Pages](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/)
- [GrapesJS Storage Manager](https://grapesjs.com/docs/modules/Storage.html)

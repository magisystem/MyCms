# Database: SQL Server con EF Core

## Decisione

Il CMS usa **Microsoft SQL Server** come unico database relazionale supportato, tramite EF Core 10 (vedi [ADR-0012](../decisions/ADR-0012-sql-server-only.md), che supera la parte multi-provider di ADR-0003).

PostgreSQL non è supportato e non va introdotto (provider, migrazioni, test o CI) senza un nuovo ADR.

## Configurazione

- Un solo provider: `Microsoft.EntityFrameworkCore.SqlServer`, con versione allineata alla major di EF Core 10 e fissata in `Directory.Packages.props`.
- Connection string in `ConnectionStrings:Predefinita` (nome indicativo, da fissare al bootstrap). Segreti fuori dal repository (user-secrets in sviluppo, environment variables o secret manager in produzione).
- Una sola serie di migrazioni EF Core, con il relativo model snapshot, nel progetto Infrastructure. Ogni PR che modifica lo schema aggiorna il modello, genera la migrazione, la revisiona a mano e verifica che si applichi a un database vuoto.
- Il codice provider-specifico (EF Core SQL Server, migrazioni, eventuale SQL raw) resta in Infrastructure. Domain e Application non dipendono da EF Core né da tipi SQL Server.

## Nomi delle tabelle

Tabelle e colonne in italiano, PascalCase, tabelle al plurale, con mappatura esplicita (`ToTable`), ADR-0017. Convenzioni ed elenco in [`domain-model.md`](domain-model.md#denominazione-in-italiano).

## Compatibilità del modello dati

- Identificatori `Guid` generati dall'applicazione, preferibilmente sequenziali (`Guid.CreateVersion7()`), per limitare la frammentazione degli indici cluster. Configurare esplicitamente chiavi e indici.
- Importi con `decimal`: precisione e scala esplicite (ad esempio `decimal(18,4)`) e valuta sempre memorizzata accanto all'importo.
- Istanti in UTC con `datetime2` (o `datetimeoffset` se serve conservare l'offset); non confondere l'istante UTC con il fuso orario del sito.
- Slug normalizzati in forma canonica lato applicazione, con indice univoco composto tenant/sito/(locale). Non basare la correttezza degli slug sulla collation del database.
- Concorrenza ottimistica: colonna `rowversion` mappata come token di concorrenza EF Core sulle entità modificabili (`Pagina`, ecc.).
- FK composite con `IdTenant` per impedire relazioni cross-tenant. SQL Server rifiuta cicli e percorsi di cascade multipli (ad esempio `Pagina` ↔ `RevisionePagina`): impostare `DeleteBehavior` esplicito e non usare cascade sui puntatori `IdUltimaRevisioneBozza`/`IdRevisionePubblicata`.
- Indici univoci su colonne nullable (es. `IdLingua`): in SQL Server i NULL sono considerati uguali tra loro; decidere esplicitamente se usare una colonna non nullable, un valore sentinella o un indice filtrato.
- Query raw SQL ammesse solo in Infrastructure, sempre parametrizzate e coperte da test (ad esempio per il claim dei messaggi outbox con `UPDLOCK, READPAST`).

### Documenti JSON

I documenti CMS (`ProgettoEditorJson`, `DocumentoRenderJson`, `SeoJson`, `ImpostazioniJson`) sono salvati come testo (`nvarchar(max)`), serializzati e validati dall'applicazione. Non richiedere query dentro il JSON nella prima versione. Il testo viene conservato così com'è, quindi hash ed ETag delle revisioni sono deterministici. Se in futuro si userà il tipo `json` nativo, serve un ADR e la verifica che i round-trip del builder restino invariati.

## Test

- La suite di integrazione usa **SQL Server reale** (container o istanza dedicata), non EF Core InMemory né SQLite, che non verificano vincoli, transazioni, concorrenza e migrazioni.
- La suite deve coprire: applicazione di tutte le migrazioni su database vuoto; vincoli, indici univoci e relazioni tenant/sito; query EF Core importanti, ordinamento e paginazione; transazioni, concorrenza ottimistica e outbox; persistenza e recupero dei documenti JSON; isolamento cross-tenant.
- InMemory/SQLite sono ammessi solo per test isolati non relazionali, con scopo dichiarato nel test.

## CI

La pipeline avvia un'istanza SQL Server effimera, applica le migrazioni da zero ed esegue le suite di integrazione. Un test saltato non vale come test passato.

## Cambiare database in futuro

Un eventuale secondo provider richiede un ADR che ne definisca costo, migrazioni, test e CI. Non va anticipato con astrazioni preventive: la separazione Domain/Application/Infrastructure è sufficiente a mantenere l'opzione aperta.

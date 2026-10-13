# Report di fattibilità: nuova struttura dei progetti (MyCms.*)

- **Data:** 2026-10-13
- **Stato:** proposta, **nessun codice modificato**. Richiede un nuovo ADR (vedi «Conflitti con le decisioni vigenti»).
- **Richiesta del committente:** namespace base `MyCms`; progetti `Core`, `Data`, `SqlServer`, `PostgreSql`, `Emails`, `Extender`, `Models`, `Services` e la UI `MyCms`; niente DTO (si usano direttamente le entità EF).

## 1. Verdetto

**Fattibile**, con tre avvertenze che cambiano costo e rischio:

1. **PostgreSQL non è una riorganizzazione: è una funzione nuova** e ribalta [ADR-0012](../decisions/ADR-0012-sql-server-only.md) (SQL Server unico database). Il codice oggi usa in più punti sintassi e comportamenti propri di SQL Server (§4). Si può predisporre la struttura (progetto `MyCms.PostgreSql` vuoto) subito, ma farlo funzionare e testarlo è un lavoro a parte.
2. **«Niente DTO» è la scelta con più rischio di sicurezza**, non di fattibilità (§5). Si può fare, con regole precise sui campi sensibili.
3. **Una ricerca-e-sostituzione `DvCms` → `MyCms` rompe i segreti già cifrati**: lo scopo di protezione dei segreti è la stringa `DvCms.SegretiTenant.v1` (e il nome applicazione `DvCms.Segreti`). Se cambia, ogni segreto salvato (password SMTP, credenziali PayPal/Nexi, app OAuth email, token) diventa illeggibile. Va rinominato tutto **tranne** quelle due stringhe (§6).

Il momento è favorevole: non c'è ancora un ambiente di produzione né dati da migrare.

## 2. Com'è oggi

| Progetto | File | Righe | Contenuto |
|---|---:|---:|---|
| `DvCms.Domain` | 39 | 2.900 | 45 entità e regole pure (stati ordine, IVA, IBAN…), nessuna dipendenza |
| `DvCms.Application` | 66 | 5.900 | 51 interfacce di servizio e 135 `record` (viste, dati in ingresso, esiti), solo dipende da Domain |
| `DvCms.Infrastructure` | 96 | 17.200 | EF Core + SQL Server, migrazioni, servizi, email, pagamenti, accessi, cache, outbox, seed |
| `DvCms.Web` | 90 | 7.000 | Razor Pages, middleware, endpoint dei webhook, comandi `migra`/`seed` |
| Test | 135 | 28.200 | 646 unitari, 28 di isolamento, 963 di integrazione su SQL Server reale |

Le migrazioni (37, più lo snapshot di circa 3.000 righe) stanno in `Infrastructure/Persistenza/Migrazioni`. La UI tocca il database direttamente solo in due file (controlli di salute e comando `migra`): tutto il resto passa dai servizi.

## 3. Mappa proposta: dove va cosa

| Nuovo progetto | Contenuto | Provenienza |
|---|---|---|
| `MyCms.Extender` | metodi di estensione e utility generiche (testo, date, collezioni, `Identificativi`) | `Domain/Identificativi.cs`, piccoli helper sparsi |
| `MyCms.Models` | le 45 entità (POCO, senza riferimento a EF) **con** le regole pure che vivono accanto ad esse (transizioni d'ordine, `CondizioniContrassegno`, `Iban`, aliquote IVA) | `Domain` |
| `MyCms.Core` | **astrazioni e logica senza database**: `IContestoTenant`, normalizzatore host, localizzatore (interfaccia), protettore segreti, hash password (BCrypt), TOTP (QRCoder), opzioni/impostazioni, esiti (`EsitoOperazione`), schema e validazione del documento del builder | `Application/Tenancy`, `Localizzazione`, parte di `Accessi`, `Segreti`, `Builder` |
| `MyCms.Data` | `ContestoDatabase`, configurazioni EF, filtri di query per tenant, `RisolutoreTenantDatabase`, interceptor di invalidazione cache, seed, `ConvertitoreDataOraUtc`, comando `migra` | `Infrastructure/Persistenza`, `Tenancy`, `Popolamento`, `Cache` |
| `MyCms.SqlServer` | migrazioni SQL Server, snapshot, fabbrica di progettazione, i punti SQL specifici di SQL Server dietro un'interfaccia | `Infrastructure/Persistenza/Migrazioni` |
| `MyCms.PostgreSql` | migrazioni e specifiche PostgreSQL (vuoto finché non si fa il lavoro del §4) | nuovo |
| `MyCms.Emails` | canali Graph, Gmail, SMTP, SendGrid e OAuth, outbox e suo elaboratore, **template** delle email (oggi testi in codice, vedi sotto) | `Infrastructure/Email`, `Outbox`, `Application/Email` |
| `MyCms.Services` | tutti i servizi che leggono e scrivono il database: accessi, CMS, media, negozio, pagamenti (client PayPal/Nexi compresi), piattaforma, manutenzione, temi | `Application` (contratti) + `Infrastructure` (implementazioni) |
| `MyCms` | Razor Pages, middleware, endpoint dei webhook, `Program.cs`, scelta del provider | `Web` |

**Direzione delle dipendenze** (nessun ciclo): `Extender` ← `Models` ← `Core` ← `Data` ← `Emails` e `Services` ← `MyCms`. `SqlServer` e `PostgreSql` dipendono da `Data` e `Models`; la UI li referenzia entrambi e sceglie a runtime (`Database:Provider`) con `MigrationsAssembly`. L'accodamento delle email passa da un'interfaccia in `Core` (`IAccodatoreEmail`), così `Services` non dipende da `Emails`.

Note sulla mappa:
- **Il template delle email non esiste ancora come cosa a sé**: oggi i messaggi sono testo composto in codice (`NotificheOrdine`, conferme, recupero password). `MyCms.Emails` può ospitare i modelli, ma prima va deciso il formato (testo, HTML, motore di template): è lavoro nuovo, non uno spostamento.
- **Non assegnati nella tua lista:** pagamenti (client HTTP PayPal/Nexi), cache, temi, media/archivio file, manutenzione periodica, builder. Propongo: pagamenti e negozio in `Services` (cartelle per modulo), cache e interceptor in `Data`, temi e media in `Services`, schema del builder in `Core`. Da confermare.
- `Core` e `Extender` hanno confini sfumati: serve una regola scritta (es. «`Extender` = solo metodi di estensione senza dominio; `Core` = tutto il resto generico»).
- `Services` diventa il progetto più grande (circa 12.000 righe, oggi sparso fra Application e Infrastructure). Perde il confine fisico fra moduli (negozio, CMS, accessi): [ADR-0001](../decisions/ADR-0001-modular-monolith.md) chiede un monolite modulare. Mitigazione: cartelle e namespace per modulo e test di architettura che vietano i riferimenti incrociati non voluti; si può dividere in seguito senza toccare la UI.

## 4. PostgreSQL: cosa c'è da fare davvero

Oltre alle migrazioni e al provider (`Npgsql.EntityFrameworkCore.PostgreSQL`), il codice attuale dipende da SQL Server in questi punti:

| Punto | Dove | Cosa serve per PostgreSQL |
|---|---|---|
| Prenotazione dei messaggi outbox con `UPDLOCK, READPAST` e `OUTPUT inserted` | `ElaboratoreOutbox` (unica query SQL grezza) | riscrivere con `FOR UPDATE SKIP LOCKED` / `RETURNING`; interfaccia per provider |
| Rilevamento dei deadlock (errore 1205) | `RiprovaSuDeadlock` | codice `40P01` di Npgsql; usato su ordini, pagamenti, giacenze |
| Token di concorrenza `rowversion` (`byte[] TokenConcorrenza`, 45 file) | entità e UI (token in base64 nei moduli) | PostgreSQL non ha `rowversion`: colonna di sistema `xmin` (tipo diverso) o trigger; cambia il tipo della proprietà o richiede una conversione |
| Indici filtrati e vincoli `CHECK` scritti con `[Colonna]` | 33 punti nelle configurazioni EF | quoting diverso (`"Colonna"`): configurazione per provider |
| Maiuscole/minuscole e collation | confronti su host, email, slug, nomi | SQL Server è case-insensitive di default, PostgreSQL no: serve un controllo di tutte le query che confrontano testo |
| Tipi: `datetime2`, `nvarchar`, `decimal`, `char(3)` | documentato in `database.md` | mappatura equivalente (`timestamptz`, `text`, `numeric`): verificare |
| Test | 963 di integrazione su SQL Server reale | esecuzione anche su PostgreSQL (container), CI a matrice, tempi raddoppiati (circa 13 minuti a giro) |

Il costo ricorrente è quello già indicato da ADR-0012: due serie di migrazioni da tenere allineate (ogni modifica del modello, due migrazioni) e due motori da testare. **Raccomandazione:** fare prima la ristrutturazione con il solo SQL Server (e il progetto `MyCms.PostgreSql` come segnaposto), poi il supporto PostgreSQL come tappa separata.

## 5. «Niente DTO»: cosa significa

Oggi i servizi restituiscono 135 tipi di sola lettura (viste, dati in ingresso, esiti) e le pagine non vedono le entità. Usare le entità direttamente è fattibile, ma:

- **Segreti e hash nelle entità:** `Utenza` (hash della password, impronte del secondo fattore), `ImpostazioniEmailTenant`, `ImpostazioniPagamentoSito`/`Nexi`/`Oauth` (segreti cifrati), token di recupero. Un campo reso o serializzato per errore è una fuga di dati. Regola proposta: **le entità non si legano mai ai moduli** (`[BindProperty]` solo su piccoli modelli di modulo nella UI, che restano), e le query che le restituiscono alla UI escludono i campi sensibili.
- **Over-posting e tenant:** oggi `IdTenant`/`IdSito` non possono arrivare dal client perché i servizi non li accettano (regola 5 di `AGENTS.md`). Con le entità in ingresso quel rischio torna: i servizi devono continuare a prendere dati espliciti e impostare i campi sensibili da sé.
- **Proiezioni e tracking:** gli elenchi che oggi leggono solo le colonne necessarie caricherebbero entità intere (più dati, tracciamento); si usa `AsNoTracking` e `Select` dove serve.
- **Esiti e snapshot:** `EsitoOperazione`, i preventivi (`PreventivoPagamento`) e gli snapshot degli ordini non sono entità e restano.

Proposta di compromesso: eliminare i DTO che sono copie 1:1 di un'entità senza campi sensibili (cataloghi, spedizioni, categorie, ordini), **mantenere** modelli di modulo e viste ridotte per gli aggregati con segreti (accessi, canali email, credenziali di pagamento). Si fa modulo per modulo.

## 6. Rinomina `DvCms` → `MyCms`

Mechanical ma ampia: soluzione, 7 progetti, ogni `namespace`/`using`, file di progetto, workflow CI (`DvCms.sln`), `launchSettings`, 25 documenti, `AGENTS.md`/`CLAUDE.md` (regola 16: «il nome del progetto è DvCms», ADR-0017).

Da **non** cambiare, o cambiare con migrazione:
- `DvCms.SegretiTenant.v1` e `SetApplicationName("DvCms.Segreti")`: cambiarli rende illeggibili i segreti già salvati.
- Nomi dei cookie (`.DvCms.Admin`, `.DvCms.Cliente`, `.DvCms.SecondoFattore`, …) e `SetApplicationName("DvCms.Web")`: cambiano e **chiudono tutte le sessioni** (accettabile prima della produzione).
- Nomi dei contatori (`DvCms.Web`, `DvCms.Cache`, `DvCms.Outbox`): cambiano le serie di metriche (nessun cruscotto ancora costruito).
- Le tabelle e la tabella di storico migrazioni (`StoricoMigrazioni`) non cambiano nome; le migrazioni esistenti restano valide perché identificate dal loro `MigrationId`, anche se cambiano namespace e assembly.

## 7. Conflitti con le decisioni vigenti (serve un ADR prima del codice)

- **ADR-0012 / `database.md`:** «PostgreSQL non è supportato e non va introdotto senza un nuovo ADR» → nuovo ADR che lo supera, con la motivazione e la politica di test.
- **ADR-0001 (monolite modulare) e struttura a strati Domain/Application/Infrastructure** (regola 3b, test `RegoleDipendenzaProgettiTest`) → nuova struttura e nuove regole di dipendenza; il test di architettura va riscritto.
- **ADR-0017 e regola 16** (nome DvCms) → aggiornare.
- **Regola 5/13** (tenant e ruoli dal server): invariate, ma da riverificare con le entità in ingresso (§5).

## 8. Impatto su test, CI e strumenti

- I 135 file di test cambiano namespace e riferimenti; `UnitTests` oggi referenzia Domain/Application/Infrastructure e dovrà referenziare i nuovi progetti. Nessun test cambia comportamento.
- CI: percorsi e nome della soluzione; con PostgreSQL, un servizio database in più e una matrice.
- `dotnet ef`: due progetti di migrazione, con `--project` diverso per provider e un `DbContext` di progettazione per ciascuno.
- Il formato del codice (`dotnet format`) e gli analizzatori restano come sono.

## 9. Piano a tappe (ogni tappa compila e supera tutti i test)

0. **ADR e documenti** (nuova struttura, supera ADR-0012, rinomina).
1. **Rinomina** `DvCms` → `MyCms` senza spostare nulla (salvo le stringhe protette e i cookie, §6).
2. **`Models`** (da Domain) e **`Extender`**.
3. **`Core`** (astrazioni, sicurezza, localizzazione, tenancy, schema del builder).
4. **`Data`** (contesto, configurazioni, filtri, seed, risolutore tenant, interceptor).
5. **`SqlServer`** (migrazioni in assembly separato, `MigrationsAssembly`) e `Program.cs` che sceglie il provider.
6. **`Emails`** (canali, outbox, modelli).
7. **`Services`** (fusione di Application e Infrastructure per modulo; eliminazione dei DTO a partire dai moduli senza segreti).
8. **UI** → progetto `MyCms`.
9. **PostgreSQL** come tappa separata (§4).

Si usa `git mv` per conservare la storia dei file. Dimensioni relative: tappe 1 e 2 piccole/medie e meccaniche; 3–5 medie; 6 media; 7 grande (il grosso del lavoro e del rischio); 8 piccola; 9 grande.

## 10. Decisioni che servono da te

1. **PostgreSQL:** confermi di superare ADR-0012? Subito (con la tappa 9 dopo la ristrutturazione) o solo il segnaposto per ora? Quanto deve essere testato (CI su entrambi)?
2. **DTO:** va bene il compromesso del §5 (entità direttamente, tranne modelli di modulo e viste per gli aggregati con segreti)?
3. **Moduli non elencati:** confermi l'assegnazione proposta nel §3 (pagamenti, negozio, cache, temi, media, builder)?
4. **Nome e segreti:** confermi che i segreti già cifrati e i cookie seguono il §6 (nessuna perdita di segreti; sessioni chiuse)?
5. **Template email:** vuoi che la tappa 6 includa un vero sistema di modelli (testo/HTML, per lingua) o solo lo spostamento del codice attuale?
6. **`Services` unico:** va bene un solo progetto per ora, con possibilità di dividerlo per modulo in seguito?

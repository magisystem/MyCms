# Piano M0/M1

Piano di implementazione richiesto da `docs/AI_START_HERE.md`, preparato prima di scrivere codice. Segue `AGENTS.md`, la roadmap e gli ADR. Non contiene stime (vedi `roadmap.md`). Da approvare prima di iniziare.

## Avanzamento

| Voce | Stato |
|---|---|
| A-001 Solution e CI | Completata |
| A-002 SQL Server ed EF Core | Completata (migrazione iniziale vuota; le tabelle arrivano con B-001 e A-009) |
| A-003 Convenzioni e logging | Completata (`docs/development/convenzioni-codice.md`) |
| A-009 Localizzatore | Completata |
| A-004 Layout Tabler | Da fare |

## 1. Stato attuale del repository

Verificato sul branch `claude/dreamy-gates-h43tjs`:

- Solo documentazione (`README`, `AGENTS`, `CLAUDE`, `GEMINI`, `CONTRIBUTING`, `docs/`) e il kit del tema in `admin-theme/` (`tabler`, `tabler-icons`, `tabler-avatars`, `tabler-emails`, `tabler-illustrations`, circa 500 MB).
- Nessuna solution, nessun progetto, nessun test, nessuna CI, nessun `global.json`, `.editorconfig` o `Directory.*.props`. Il `.gitignore` è il template Visual Studio.
- Nell'ambiente cloud in cui è stato preparato questo piano non è installato il .NET SDK (`dotnet` assente); è presente Docker. In quell'ambiente la build e i test .NET non sono eseguibili senza installare l'SDK. Va verificato prima di iniziare l'implementazione, perché `AGENTS.md` vieta di dichiarare test eseguiti se non lo sono.

## 2. Incoerenze trovate e correzioni applicate in questo aggiornamento

| Incoerenza | Correzione |
|---|---|
| Il recupero password (M1) richiedeva outbox minimo ed email, che erano pianificati in M2 | Nuovo milestone **M1b** (email, outbox minimo, recupero password) dopo M1; l'outbox minimo esce da M2 (`roadmap.md`) |
| `AGENTS.md` descrive la prima milestone senza recupero password, la roadmap M1 lo includeva | Allineati: M1 non include il recupero password |
| Nessuna regola su come un nuovo `TenantAdmin` riceve le credenziali iniziali (senza email in M1) | Proposta in sezione 3, da confermare |
| B-004 (test isolamento) citava i media, che arrivano in M3 | Resta valido come matrice che si estende a ogni nuova area; in M1 copre pagine, account e domini |

## 3. Decisioni da prendere prima di iniziare

Bloccanti per M0/M1, con proposta di default (da confermare):

1. **Framework di test:** xUnit. Un solo framework.
2. **Credenziali iniziali del `TenantAdmin` in M1** (l'email arriva solo in M1b): il `PlatformAdmin` imposta una password temporanea mostrata una sola volta, con cambio obbligatorio al primo accesso. In M1b si aggiunge l'invito via email.
3. **Email uguale fra `PlatformAdmin` e account di tenant:** rifiutare, nel caso d'uso di creazione, un `TenantAdmin` o `Utente` con la stessa email di un `PlatformAdmin`, per evitare ambiguità nel login (ADR-0011).
4. **SQL Server in sviluppo e CI:** connection string da environment variable; in CI un service container SQL Server (immagine Microsoft); in locale un'istanza o un container. Niente Testcontainers in M0 (dipendenza evitabile, regola 11); rivalutabile.
5. **Logging:** `ILogger` con provider console strutturato in M0 e correlation ID; il provider APM resta una decisione di hosting.
6. **Identificatori:** `Guid.CreateVersion7()` generati dall'applicazione (`database.md`).
7. **Versione dell'SDK:** `global.json` con l'SDK .NET 10 corrente; versione esatta fissata al bootstrap dopo la verifica dei pacchetti.

Non bloccanti per M0/M1 (restano aperte): ADR-0005 e ADR-0007 (servono da M4), hosting, RPO/RTO, policy password dettagliate, provider email per tenant (M1b), 2FA.

## 4. M0 — Bootstrap

Struttura minima (si crea il necessario, non tutta la struttura raccomandata):

```text
DvCms.sln
global.json  Directory.Build.props  Directory.Packages.props  .editorconfig
src/DvCms.Web  src/DvCms.Domain  src/DvCms.Application  src/DvCms.Infrastructure
tests/DvCms.UnitTests  tests/DvCms.IntegrationTests  tests/DvCms.TenantIsolationTests
.github/workflows/ci.yml
```

Passi, uno per PR (CONTRIBUTING: PR piccole e verificabili):

1. **A-001 — Solution e CI.** Progetti, nullable abilitato, analyzer, `.editorconfig`, versioni NuGet centralizzate, workflow di restore/build/test. `.gitignore` esteso per `appsettings.Production.json` e simili.
2. **A-002 — SQL Server ed EF Core.** `DbContext` in Infrastructure, connection string da configurazione, prima migrazione, smoke test di integrazione che applica le migrazioni su database vuoto.
3. **A-003 — Convenzioni.** Logging strutturato, correlation ID, convenzioni UTC (`datetime2`), `Guid` v7.
4. **A-009 — Localizzatore dei testi statici (ADR-0018).** Tabelle `Lingue` e `TestiStatici`, `ILocalizzatore` e `@_L["TESTO"]`, algoritmo di creazione delle righe mancanti, cache, controllo che gli argomenti siano costanti letterali; deve precedere il layout, così ogni testo nasce già localizzato.
5. **A-004 — Layout admin Tabler.** Copia integrale di `tabler` e `tabler-icons` in `src/DvCms.Web/wwwroot/lib/`; regola dei file statici che non serve le pagine HTML demo; layout Razor condiviso (menu laterale, icone); nessun asset di `tabler-emails`, `tabler-illustrations`, `tabler-avatars`.

**Exit criteria (roadmap):** build pulita, test eseguibile in CI, migrazione applicabile da zero, layout Tabler renderizzato, setup locale aggiornato in `local-setup.md`.

Test M0: smoke di avvio, migrazione su database vuoto, test che `GET` di una pagina demo di Tabler restituisce 404 mentre un asset CSS è servito.

## 5. M1 — Vertical slice tenant, sito, pagina

Ordine di PR:

6. **B-001 — Modello `Tenant`/`Sito`/`DominioSito`/`ProfiloGuest`.** Vincoli (dominio univoco, stati), FK composite con `IdTenant`, nessun cascade sui cicli, migrazione.
7. **B-002 — Risoluzione host.** Normalizzazione (case, porta ignorata), solo host registrati, verificati e attivi; risposta controllata per host ignoti; `ContestoTenant` request-scoped; alias `localhost` solo in `Development`; header forwarded da proxy configurati.
8. **B-003 — Account locali e autenticazione (ADR-0016).** Tabella `Utenze` propria con vincolo di ruolo/tenant e indice univoco `(IdTenant, EmailNormalizzata)`; hash password BCrypt (`BCrypt.Net-Next`, work factor 12, pre-hash, rehash al login); cookie authentication con `TimbroSicurezza` verificato a ogni richiesta, schema e cookie dell'area admin con percorso `/admin`; blocco di 5 minuti dopo 5 tentativi falliti, rate limiting ed errore uniforme al login. Niente ASP.NET Core Identity: scompare il rischio dell'unicità globale, ma l'autenticazione è codice nostro, quindi test mirati (hash, rehash, blocco, invalidazione sessioni, login incrociato) e inclusione nella security review.
9. **B-005 — Seeding.** Seeder idempotente del tenant primario (`cms.davidemartelli.dev`, `Primario`), del sito, dei domini e del primo `PlatformAdmin`, con dati da `Seed:TenantPrimario`, password da segreto, alias `localhost` solo in `Development` e tenant di test opzionali (`cms1`/`cms2`) solo in `Development`; lanciabile da comando o all'avvio (opt-in); mai riscrive dati esistenti.
10. **B-006/B-007/B-010 — Menu "Platform".** CRUD tenant, siti e domini (verifica manuale registrata), creazione `TenantAdmin` solo dal `PlatformAdmin`, ingresso nell'admin di un tenant e azioni in `RegistroAudit`; schermata "Testi" per modificare i testi statici (solo `PlatformAdmin`).
11. **C-001/C-002/C-003 (versione minima).** `Pagina` con `IdRevisionePubblicata` e revisione, creazione/modifica nell'admin con validazione server e antiforgery, slug univoco per sito (slug riservati `admin`, `lib`), rendering pubblico Razor per host e slug (404 per bozze e inesistenti).
12. **B-004 — Matrice di isolamento.** Tenant A/B su pagine, account e domini.

Non fanno parte di M1: recupero password, email, outbox, `Utente` e registrazione, e-commerce, builder, temi dedicati, media.

**Exit criteria (roadmap):** un `PlatformAdmin` crea due tenant (`cms1`/`cms2.davidemartelli.dev` in locale) con i rispettivi `TenantAdmin`; i due tenant pubblicano lo stesso slug su siti e domini distinti; nessun accesso incrociato; tentativi di escalation respinti.

## 6. M1b — Email e recupero password

Dopo M1, perché dipende dall'outbox: contratto email con adapter SMTP (A-005), configurazione email del tenant (B-009), outbox minimo (C-007), recupero password in due email (B-008). Gli adapter Graph, Gmail e SendGrid (A-006, A-007, A-008) seguono prima del primo cliente.

## 7. Test previsti

Unit: normalizzazione host e slug, regole di escalation dei ruoli, stati di pagina, validazione dei campi.

Integrazione (SQL Server reale): migrazioni da zero; vincoli (dominio, slug per sito, email per tenant ripetibile fra tenant); risoluzione host (ignoto, porta ignorata, `localhost` solo in `Development`, non risolve in `Production`); login (credenziali giuste/errate, `TenantAdmin` dall'host di un altro tenant fallisce con l'errore uniforme, `PlatformAdmin` da qualunque host); autorizzazione del menu "Platform" (403/404 per `TenantAdmin`); creazione `TenantAdmin` solo dal `PlatformAdmin`, rifiuto dell'auto-elevazione; audit di ingresso e azioni; pubblicazione (bozza mai pubblica, pubblicata visibile, 404 corretto).

Isolamento (`DvCms.TenantIsolationTests`): con Tenant A e B, lettura/modifica/cancellazione di pagine e account altrui negate, stesso slug su siti diversi senza conflitti, nessun `IdTenant` dal client accettato.

Ogni PR riporta comandi ed esiti reali dei test eseguiti.

## 8. Rischi

- **Autenticazione scritta in proprio** (punto 7): medio-alto; mitigato dalla libreria BCrypt, test mirati e security review. Il rischio precedente di Identity multi-tenant non esiste più.
- **Host header e proxy:** `localhost` o host forgiati dietro il reverse proxy; mitigato da registrazione degli host, alias solo in `Development` e proxy attendibili configurati.
- **Stessa origine per admin e sito pubblico:** cookie distinti con percorso limitato, CSP e sanitizzazione (da M2 con il rich text).
- **Dimensione di `admin-theme/` e di `wwwroot`:** repository e artefatti di build pesanti; accettato dal committente, da monitorare in CI.
- **SDK non disponibile in ambienti di sviluppo/CI cloud:** verificare l'installazione di .NET 10 all'inizio.

## 9. Cosa non si fa

Nessun e-commerce, builder, tema dedicato, media, 2FA o strumenti di reset della password del `PlatformAdmin`. Nessun ASP.NET Core Identity. Nessun codice provider-specifico fuori da Infrastructure. Nessun pacchetto aggiunto senza motivazione e controllo della licenza.

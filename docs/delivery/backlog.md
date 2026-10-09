# Backlog iniziale

Le voci sono candidate a issue GitHub. Ogni issue deve essere piccola, verificabile e referenziare la milestone. Gli agenti devono completare una voce alla volta.

## EPIC A — Bootstrap

### A-001: Creare solution e CI
**Accettazione:** solution compila in .NET 10; workflow CI esegue restore/build/test; nessun secret nel repository; `.gitignore` esclude i file di configurazione di ambiente con segreti (es. `appsettings.Production.json`).

### A-002: Configurare SQL Server ed EF Core
**Accettazione:** connection string da configurazione/segreti; una serie di migrazioni EF Core che si applica a un database vuoto; smoke test d'integrazione su SQL Server reale, configurabile via environment variable.

### A-003: Definire convenzioni e logging
**Accettazione:** nullable abilitato, logging strutturato, correlation ID e convenzioni UTC documentate.

### A-005: Contratto email e adapter
**Accettazione:** contratto unico `IEmailSender` in Application; selezione del provider dal tenant (`EmailProvider`); adapter SMTP come primo; provider sconosciuto o impostazioni incomplete rifiutati; invio dal worker tramite outbox con `TenantId` nel messaggio; fake nei test; nessun segreto nel repository. Gli adapter Microsoft Graph, Gmail API e SendGrid sono voci successive (A-006, A-007, A-008) con test di contratto comune.

### B-009: Configurazione email del tenant (menu Platform)
**Accettazione:** nella schermata di configurazione del tenant (solo `PlatformAdmin`) un selettore del provider (`Smtp`, `Graph`, `Gmail`, `SendGrid`) mostra solo i campi del provider scelto; validazione lato server; segreti cifrati nel database, in sola scrittura nella UI e mai nei log; invio di prova; un tenant senza configurazione valida non invia e l'errore è visibile ai `PlatformAdmin`; chiave di cifratura da configurazione di ambiente; test.

### A-006: Adapter Microsoft Graph
**Accettazione:** invio tramite Graph con credenziali da configurazione; test con fake; documentate le attività manuali (registrazione app, permessi).

### A-007: Adapter Gmail API
**Accettazione:** invio tramite Gmail API; test con fake; documentate le attività manuali (progetto OAuth, eventuale delega di dominio).

### A-008: Adapter SendGrid
**Accettazione:** invio tramite SendGrid; test con fake; documentata la configurazione dell'account e del dominio mittente.

### A-004: Layout admin Tabler
**Accettazione:** le cartelle `tabler` e `tabler-icons` di `admin-theme/` sono copiate per intero in `src/DvCms.Web/wwwroot/lib/`; le pagine HTML demo non sono servite al pubblico (test che `GET` di una pagina demo restituisce 404); layout Razor condiviso con menu laterale; nessun CDN esterno; nessun file di `tabler-emails`, `tabler-illustrations`, `tabler-avatars` in `wwwroot`.

## EPIC B — Tenant e accessi

### B-001: Modello Tenant/Site/Domain
**Accettazione:** vincoli e relazioni impediscono domini duplicati; tenant e site hanno status; unit/integration test presenti.

### B-002: Risoluzione host
**Accettazione:** solo host registrati risolvono; host sconosciuti danno una risposta controllata; proxy forwarded configurati esplicitamente; il `TenantId` deriva solo dall'host e nessun valore del client lo sostituisce; la porta è ignorata; `localhost` risolve il tenant di gestione solo in ambiente `Development` (test che in Production non risolve).

### B-003: Account locali e autenticazione
**Accettazione:** tabella `Utenze` propria e nessun pacchetto o tabella di ASP.NET Core Identity (ADR-0016); hash BCrypt (work factor 12, pre-hash, lunghezza massima, rehash al login); blocco di 5 minuti dopo 5 tentativi falliti consecutivi, azzerato dall'accesso riuscito; `SecurityStamp` verificato a ogni richiesta (cambio password o disabilitazione invalidano le sessioni); login/logout; ogni account `TenantAdmin` appartiene a un solo tenant e un tenant può averne più di uno; flag `PlatformAdmin` globale; email unica per tenant e ripetibile fra tenant (test con la stessa email su due tenant); principal `Guest` legato al tenant risolto dall'host (profilo `TenantGuestProfile` creato con il tenant); un `TenantAdmin` accede solo al proprio tenant, il `PlatformAdmin` a tutti con i poteri di un `TenantAdmin`; l'account è cercato solo nel tenant dell'host e un `TenantAdmin` che accede dall'host di un altro tenant fallisce con l'errore delle credenziali errate (test); il `PlatformAdmin` accede dall'host di qualunque tenant; schemi/cookie di autenticazione distinti per admin e area cliente; ogni admin request verifica ruolo e tenant dell'account rispetto alla risorsa; nessuna registrazione per account amministrativi.

### B-005: Seeding del tenant primario e del primo PlatformAdmin
**Accettazione:** seeder idempotente lanciabile da comando e, se `Seed:RunOnStartup`, all'avvio (default falso); dati del tenant primario (`IsPrimary`), sito, domini (con flag "solo sviluppo") e `PlatformAdmin` letti da `Seed:PrimaryTenant`; password del `PlatformAdmin` da segreto, variabile d'ambiente o richiesta interattiva, mai da file versionati, errore se manca; non sovrascrive dati esistenti né reimposta password, avvisa nel log se i dati divergono; in `Development` seme opzionale dei tenant di test da `Seed:DevelopmentTenants` (ignorato altrove); alias `localhost` solo in `Development`; voce di `AuditLog`; nessuna password nei log; il `PlatformAdmin` può poi accedere dal dominio del tenant primario (`cms.davidemartelli.dev`) e creare altri tenant; nessuna funzione di reset password (gestione manuale dell'operatore); test.

### B-006: Menu "Platform" e gestione tenant
**Accettazione:** il `PlatformAdmin` apre l'admin di qualunque tenant e vede il menu "Platform" (area Razor protetta lato server, non solo nascosta); CRUD tenant, siti e domini (verifica manuale: chi e quando); sospensione/riattivazione tenant; ingresso in un tenant e operazioni registrati in `AuditLog`; gli altri ruoli ricevono 403/404.

### B-007: Creazione TenantAdmin e non-escalation
**Accettazione:** in M1 le credenziali iniziali sono una password temporanea mostrata una sola volta con cambio obbligatorio al primo accesso (da confermare, vedi `plan-m0-m1.md`); l'invito via email arriva in M1b; solo il `PlatformAdmin` crea/eleva `TenantAdmin`; nessun ruolo si auto-assegna ruoli superiori; test automatici di escalation per UI e richieste manipolate.

### B-008: Recupero password
**Milestone:** M1b.
**Accettazione:** per `TenantAdmin` e `Utente` (non per il `PlatformAdmin`), con ricerca dell'account nel tenant dell'host; richiesta con risposta uniforme; prima email con codice OTP (scadenza breve, tentativi limitati, hash, rate limiting); dopo OTP corretto, seconda email distinta con link monouso a scadenza breve (mai mostrato a schermo); link invalidato all'uso e da nuove richieste; al cambio password sessioni invalidate e notifica email; invio via outbox; test su OTP errato/scaduto/riusato e su enumerazione.

### B-004: Test isolamento tenant
**Accettazione:** cross-tenant read/update/delete negati per pagine e media; test ripetibile in CI.

## EPIC C — Pagine e pubblicazione

### C-001: Entity Page e PageRevision
**Accettazione:** schema revisionato, draft/live separati, token concorrenza.

### C-002: CRUD pagine Razor
**Accettazione:** crea/modifica pagina, validazione server, protezione antiforgery, slug unico nel sito.

### C-003: Rendering pagina pubblicata
**Accettazione:** render server-side per host+slug; draft mai pubblico; 404 corretto.

### C-004: Preview e rollback
**Accettazione:** preview richiede autorizzazione e non è indicizzabile; publish e rollback aggiornano puntatori in transazione.

### C-006: Template guidati iniziali
**Accettazione:** almeno un template versionato con form Razor e policy di editing; template immutabili per versione; test.

### C-007: Outbox minimo
**Milestone:** M1b. **Accettazione:** il publish scrive l'evento outbox nella stessa transazione; un worker semplice processa gli eventi e invalida la cache; consegna ripetuta tollerata. La versione production-ready è G-001.

### C-005: SEO base e redirect
**Accettazione:** title/description/canonical/robots; rename slug crea redirect secondo policy; no redirect loop.

## EPIC D — Temi e media

### D-001: Theme registry
**Accettazione:** tema risolto da allow-list; settings validati; nessuna view path arbitraria.

### D-002: Due temi prova
**Accettazione:** una pagina compatibile è renderizzata con due aspetti distinti senza duplicare contenuto.

### D-003: Object storage adapter
**Accettazione:** upload e download autorizzati; oggetti scoped per tenant; file system locale per dev e provider configurabile.

### D-004: Image metadata e alt text
**Accettazione:** dimensioni/MIME/hash, alt text e limiti di upload verificati.

## EPIC E — Page builder

### E-001: Spike GrapesJS
**Accettazione:** builder montato in Razor Page, project JSON salvato e ricaricato da endpoint .NET; licenza/dependency review registrata.

### E-002: Schema document CMS
**Accettazione:** schemaVersion, nodi tipizzati, registry componenti, validazione e limiti di profondità/dimensione.

### E-003: Adapter editor/documento
**Accettazione:** conversione deterministica per componenti supportati; test round-trip; errori loss/non supportati non silenziosi.

### E-004: Policy template lato server
**Accettazione:** nodo bloccato non può essere modificato/eliminato tramite payload API manipolato; slot accetta solo tipi allow-list.

### E-005: Autosave e concorrenza
**Accettazione:** debounce client, validazione server, token concorrenza; nessuna sovrascrittura silenziosa.

### E-006: Componenti e animazioni
**Accettazione:** animazioni declarative; nessuna esecuzione di JavaScript arbitrario del tenant; prefers-reduced-motion considerato.

## EPIC F — Commerce

### F-000: Registrazione e login acquirenti
**Accettazione:** registrazione possibile solo se il sito ha e-commerce attivo (`CustomerRegistrationEnabled`), altrimenti 404; crea sempre un `Utente` del tenant, senza possibilità di scegliere il ruolo; conferma email e rate limiting; un `Utente` non accede all'admin; account separati per tenant; test cross-tenant.

### F-001: Catalogo
**Accettazione:** tenant-scoped prodotti/varianti/prezzi; denaro con decimal e valuta; test accesso.

### F-002: Carrello e calcolo totale
**Accettazione:** totali sempre ricalcolati server-side; prezzo manomesso dal client ignorato; l'ordine richiede un account autenticato (`Utente`, `TenantAdmin` o `PlatformAdmin`); il `Guest` non può ordinare.

### F-003: Ordine snapshot
**Accettazione:** ordine conserva i dati commerciali essenziali al momento dell'acquisto; le modifiche prodotto non cambiano storico ordine.

### F-004: Provider e webhook
**Accettazione:** provider deciso, signature verification, deduplica/idempotenza, test di evento duplicato e fallito.

### F-005: Inventario concorrente
**Accettazione:** due checkout concorrenti non vendono più unità di quelle disponibili secondo policy approvata.

## EPIC G — Production readiness

### G-001: Outbox e worker
**Accettazione:** evento persistito nella stessa transazione di dominio, retry idempotente e stato errori consultabile.

### G-002: Cache invalidation
**Accettazione:** publish/cambio tema/prodotto invalidano le chiavi corrette senza contaminazione tenant.

### G-003: Sitemap e robots
**Accettazione:** sitemap per sito include solo pagine canoniche pubblicate e indicizzabili.

### G-004: Backup/restore e runbook
**Accettazione:** procedura verificata su ambiente di test, con risultati documentati.

### G-006: Two-factor authentication
**Priorità minima, da affrontare per ultima; non bloccante per il go-live.** **Accettazione:** ADR su metodo, recupero e obbligatorietà per `PlatformAdmin`/`TenantAdmin`; implementazione e test; enrollment e recupero documentati.

### G-005: Security review
**Accettazione:** checklist di `docs/architecture/security.md` completata; issue critiche risolte o accettate formalmente.

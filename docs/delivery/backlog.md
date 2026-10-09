# Backlog iniziale

Le voci sono candidate a issue GitHub. Ogni issue deve essere piccola, verificabile e referenziare la milestone. Gli agenti devono completare una voce alla volta.

## EPIC A — Bootstrap

### A-001: Creare solution e CI
**Accettazione:** solution compila in .NET 10; workflow CI esegue restore/build/test; nessun secret nel repository.

### A-002: Configurare SQL Server ed EF Core
**Accettazione:** connection string da configurazione/segreti; una serie di migrazioni EF Core che si applica a un database vuoto; smoke test d'integrazione su SQL Server reale, configurabile via environment variable.

### A-003: Definire convenzioni e logging
**Accettazione:** nullable abilitato, logging strutturato, correlation ID e convenzioni UTC documentate.

### A-004: Layout admin Tabler
**Accettazione:** layout Razor condiviso basato su Tabler 1.6.1 e Tabler Icons; in `wwwroot` solo gli asset usati; nessun CDN esterno; nessun file delle cartelle `tabler-emails`, `tabler-illustrations`, `tabler-avatars`; aggiornata la nota sulle licenze.

## EPIC B — Tenant e accessi

### B-001: Modello Tenant/Site/Domain
**Accettazione:** vincoli e relazioni impediscono domini duplicati; tenant e site hanno status; unit/integration test presenti.

### B-002: Risoluzione host
**Accettazione:** solo host registrati risolvono; host sconosciuti danno una risposta controllata; proxy forwarded configurati esplicitamente.

### B-003: Identity e membership
**Accettazione:** login/logout; membership tenant con ruolo `TenantAdmin` o `Utente` e permessi; flag `PlatformAdmin`; ogni admin request verifica ruolo e membership della risorsa; nessuna registrazione self-service.

### B-005: Bootstrap del primo PlatformAdmin
**Accettazione:** comando documentato che crea il primo `PlatformAdmin` senza credenziali predefinite; rifiuta l'esecuzione se esiste già un `PlatformAdmin` salvo opzione esplicita; test.

### B-006: Area di piattaforma
**Accettazione:** area Razor visibile solo al `PlatformAdmin`; CRUD tenant, siti e domini (con verifica manuale: chi e quando); sospensione/riattivazione tenant; operazioni registrate in `AuditLog`; i non-`PlatformAdmin` ricevono 403/404.

### B-007: Gestione utenti e non-escalation
**Accettazione:** solo il `PlatformAdmin` crea/eleva `TenantAdmin`; il `TenantAdmin` crea `Utente` e assegna permessi/siti nel proprio tenant; nessuno si auto-assegna ruoli superiori; test automatici di escalation per UI e richieste manipolate.

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
**Accettazione:** il publish scrive l'evento outbox nella stessa transazione; un worker semplice processa gli eventi e invalida la cache; consegna ripetuta tollerata. La versione production-ready è G-001.

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

### F-001: Catalogo
**Accettazione:** tenant-scoped prodotti/varianti/prezzi; denaro con decimal e valuta; test accesso.

### F-002: Carrello e calcolo totale
**Accettazione:** totali sempre ricalcolati server-side; prezzo manomesso dal client ignorato.

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

### G-005: Security review
**Accettazione:** checklist di `docs/architecture/security.md` completata; issue critiche risolte o accettate formalmente.

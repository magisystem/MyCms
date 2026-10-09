# ADR-0014 — Amministrazione di piattaforma: tenant e domini manuali, certificati dall'hosting

- **Stato:** Accepted
- **Data:** 2026-10-09
- **Aggiornato:** 2026-10-09 (menu "Platform" nell'admin; registrazione solo per acquirenti; seeding del tenant primario)

## Contesto

I clienti non si registrano da soli: tenant, siti e account amministrativi sono creati dagli operatori della piattaforma. Il numero di tenant è contenuto e i domini cambiano raramente. Automatizzare la verifica dei domini sarebbe costo senza beneficio immediato; i certificati TLS sono già gestiti dall'hosting.

## Decisione

- Il `PlatformAdmin` usa la stessa interfaccia admin dei tenant. Può aprire l'admin di **qualunque tenant** e lì lavora **come un `TenantAdmin`** di quel tenant; in più vede il menu **"Platform"**, con cui gestisce tenant, siti, domini e utenti `TenantAdmin`. Il `TenantAdmin` invece accede solo al proprio tenant.
- L'admin si raggiunge dall'host del tenant (percorso riservato `/admin`); il `PlatformAdmin` può accedere dall'host di qualunque tenant (ADR-0011). Non esiste un dominio centrale di amministrazione.
- Il `PlatformAdmin` non ha recupero password: la sua password è gestita a mano dall'operatore.
- Il menu "Platform" è una Razor Area dedicata. La visibilità del menu è solo UX: ogni pagina e ogni azione dell'area è protetta lato server dalla policy `PlatformAdmin`.
- Quando il `PlatformAdmin` entra nell'admin di un tenant, il contesto tenant è esplicito e l'ingresso è registrato in `AuditLog`.
- Nessuna registrazione self-service per account amministrativi (`PlatformAdmin`, `TenantAdmin`). La registrazione degli acquirenti (`Utente`) esiste solo nei siti con e-commerce (ADR-0011).
- Solo il `PlatformAdmin` può creare utenti `TenantAdmin` o elevare un account a `TenantAdmin`, con controllo lato server. Un `TenantAdmin` non può assegnare né auto-assegnarsi il proprio ruolo o uno superiore.
- **Seeding del tenant primario.** Il tenant primario (di gestione, `IsPrimary`) e il primo `PlatformAdmin` si creano con un seeder idempotente, non da una pagina web. Poiché non esiste un dominio centrale di amministrazione e il tenant è deciso dall'host, il tenant primario ha il proprio dominio, `cms.davidemartelli.dev`: da lì il `PlatformAdmin` accede e crea gli altri tenant.
  - **Come si lancia:** da comando (`DvCms.Web seed`) oppure all'avvio se `Seed:RunOnStartup` è vero (default falso, da attivare esplicitamente in produzione).
  - **Dati:** da configurazione (`Seed:PrimaryTenant`), non dal codice: nome del tenant, sito (nome, lingua, fuso orario), elenco dei domini (host e un flag "solo sviluppo"), tema predefinito, `PlatformAdmin` (email e nome visualizzato). Il profilo Guest del tenant si crea in automatico.
  - **Segreti:** la password del `PlatformAdmin` non sta mai in file versionati né ha valori predefiniti: arriva da user-secrets, variabile d'ambiente (ad esempio `Seed__PlatformAdmin__Password`) o richiesta interattiva da comando. Se manca e il `PlatformAdmin` non esiste, il seeding fallisce.
  - **Idempotenza:** crea solo ciò che manca. Non sovrascrive dati esistenti né reimposta la password di un `PlatformAdmin` esistente; se i dati esistenti divergono dal seed, scrive un avviso nel log e non modifica nulla. Le modifiche successive si fanno dal menu "Platform".
  - **Sviluppo:** `localhost` (qualunque porta) è un dominio del tenant primario con flag "solo sviluppo", ignorato fuori da `Development` (vedi sotto). In `Development` il seed può creare anche tenant di test (ad esempio `cms1`/`cms2.davidemartelli.dev`, ciascuno con un `TenantAdmin`) da `Seed:DevelopmentTenants`; fuori da `Development` questa sezione è ignorata.
  - **Audit e log:** il seeding scrive una voce in `AuditLog`; nei log non compaiono password né segreti.
- **`localhost` solo in sviluppo.** In ambiente di sviluppo `localhost`, su qualunque porta, risolve sul tenant primario (la porta è ignorata nella normalizzazione dell'host). Non è un'eccezione alla regola "solo host registrati": `localhost` è un alias registrato dal seeding ed è abilitato solo quando l'ambiente è `Development` (o con un'impostazione esplicita, disattivata di default). In produzione `localhost` non risolve nessun tenant e un avvio con l'alias attivo fuori da Development è rifiutato.
- Alla creazione di un tenant si crea anche il suo profilo `Guest` (ADR-0011).
- **Verifica dei domini:** manuale. Il `PlatformAdmin` registra il dominio dopo averne verificato il possesso fuori banda. `SiteDomain` conserva chi ha verificato e quando; un host risolve solo se il dominio è registrato, verificato e attivo.
- **Certificati TLS:** gestiti dall'hosting tramite reverse proxy; l'applicazione non emette né rinnova certificati e non implementa ACME, e il suo sviluppo non se ne deve occupare. In produzione l'app è dietro il reverse proxy e riceve HTTP: gli header forwarded sono accettati solo da proxy configurati esplicitamente.
- Ogni operazione del menu "Platform" (creazione/sospensione tenant, domini, utenti, elevazione ruoli) viene registrata in `AuditLog` con attore e data. Non è richiesto un motivo e non esiste impersonation di altri utenti.

## Alternative considerate

- Console di piattaforma separata dall'admin: respinta, il `PlatformAdmin` deve poter passare da un tenant all'altro nella stessa interfaccia.
- Onboarding self-service con verifica DNS automatica ed ACME: rimandato finché i clienti non si registreranno da soli.
- Creazione dei tenant solo da script/DB: poco verificabile e non auditabile.

## Conseguenze

- Il flusso operativo (richiesta dominio, verifica, certificato, registrazione) va descritto nel runbook (`docs/architecture/operations.md`).
- L'accesso del `PlatformAdmin` a tutti i tenant è un privilegio forte: va protetto e auditato; la 2FA, quando implementata, è prioritaria per questo ruolo.
- Errori sui domini o scadenza dei certificati sono rischio operativo del team: servono promemoria e controlli periodici (o la verifica che l'hosting li gestisca).
- Se il modello cambia (clienti autonomi), serve un nuovo ADR che sostituisca questo.

# ADR-0014 — Amministrazione di piattaforma: tenant e domini manuali, certificati dall'hosting

- **Stato:** Accepted
- **Data:** 2026-10-09
- **Aggiornato:** 2026-10-09 (menu "Platform" nell'admin; registrazione solo per acquirenti)

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
- Il primo `PlatformAdmin` viene creato da un comando di bootstrap/seed documentato, eseguito dall'operatore, non da una pagina web. Il comando non accetta credenziali predefinite. Poiché non esiste un dominio centrale di amministrazione e il tenant è deciso dall'host, lo stesso comando crea un **tenant di gestione** dell'operatore con il suo dominio (passato come parametro): da lì il `PlatformAdmin` accede e crea gli altri tenant. Il dominio del tenant di gestione è `cms.davidemartelli.dev`.
- **`localhost` solo in sviluppo.** In ambiente di sviluppo `localhost`, su qualunque porta, risolve sul tenant di gestione (la porta è ignorata nella normalizzazione dell'host). Non è un'eccezione alla regola "solo host registrati": `localhost` è un alias registrato dal seed di sviluppo ed è abilitato solo quando l'ambiente è `Development` (o con un'impostazione esplicita, disattivata di default). In produzione `localhost` non risolve nessun tenant e un avvio con l'alias attivo fuori da Development è rifiutato.
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

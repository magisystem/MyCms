# Strategia multi-tenant

## Scelta

Database SQL Server condiviso, tabelle condivise e isolamento logico per `TenantId`. Il modello ammette più siti e domini per tenant. Non creare un database per cliente per default; prevedere una futura opzione di isolamento dedicato solo con requisiti di compliance, scala o contratto.

## Risoluzione host

1. Accettare solo host normalizzati e registrati in `SiteDomains`, verificati e attivi. La registrazione e la verifica sono manuali, a cura del `PlatformAdmin` (ADR-0014).
2. Normalizzare case, porta e formato del nome DNS secondo policy; non fidarsi di header forwarded inviati da client non attendibili.
3. Configurare esplicitamente proxy attendibili prima di utilizzare `X-Forwarded-Host` o header equivalenti.
4. Risolvere host → `SiteId` → `TenantId` tramite fonte dati attendibile.
5. Rendere il `TenantContext` request-scoped e non modificabile dall'utente della richiesta.
6. Per job/background work, includere tenant/site esplicitamente nel messaggio e verificare il contesto prima dell'esecuzione.

L'admin di ogni tenant si raggiunge dall'host del tenant stesso (percorso riservato, ad esempio `/admin`); non esiste un dominio centrale condiviso. Il tenant è deciso solo dall'host: il `TenantAdmin` del tenant A che accede dall'host del tenant B fallisce il login perché il suo account non appartiene a B. Il `PlatformAdmin` può accedere dall'host di qualunque tenant e vi lavora come un `TenantAdmin`. Il server verifica sempre ruolo e tenant dell'account rispetto al `TenantId` dell'host; nessun tenant proveniente dal client è accettato.

## Difesa a strati

- Tenant context centrale e obbligatorio per servizi tenant-scoped.
- Policy di autorizzazione tenant dell'account/ruolo.
- Filtri globali EF Core dove appropriati, con attenzione a query amministrative e bypass.
- Foreign key e indici univoci compositi per prevenire relazioni cross-tenant.
- Valutare Row-Level Security nativa di SQL Server solo come difesa aggiuntiva, con policy e test dedicati; non è l'unico controllo.
- Test d'integrazione che provano query, endpoint, media e operazioni con tenant errato.
- Chiavi cache con tenant/site e invalidazione coerente.

## Admin di piattaforma

La gestione di tenant, siti, domini e utenti `TenantAdmin` avviene nel menu "Platform" dell'admin, accessibile solo al ruolo `PlatformAdmin`, che può anche aprire l'admin di qualunque tenant (ADR-0011, ADR-0014). Ingresso in un tenant e azioni sono auditate. Nessuna registrazione self-service per gli account amministrativi.

Il `Guest` è sempre legato al tenant risolto dall'host; cookie, token di anteprima e dati del Guest non sono condivisi fra tenant. Gli account `Utente` appartengono a un solo tenant.

Le operazioni cross-tenant devono essere esposte tramite casi d'uso privilegiati e auditabili; non devono dipendere da un `IgnoreQueryFilters()` generico sparso nel codice. Gli endpoint di supporto devono verificare un ruolo di piattaforma esplicito e registrare attore e data (nessun motivo obbligatorio).

## Superfici da isolare

Pages/revisions, template custom, settings tema, asset media, prodotti, inventario, carrelli, clienti, ordini, webhook, job, export, cache, preview e audit log.

## Test obbligatori

Per almeno due tenant di test:

- `TenantAdmin` o `Utente` del tenant A che prova il login dall'host del tenant B: fallisce con lo stesso errore delle credenziali errate;
- stesso slug, stesso database: nessun conflitto se su siti differenti;
- tentativo di lettura pagina altrui: negato o non trovato secondo la policy endpoint;
- modifica/cancellazione cross-tenant: negata;
- associazione di asset/prodotto di altro tenant: rifiutata;
- cache e media non condivisi accidentalmente;
- job/outbox con tenant incoerente: scartato e segnalato;
- `TenantAdmin` o `Utente` che tenta di creare/elevare un `TenantAdmin`, di assegnarsi un ruolo superiore o di operare sul menu "Platform": negato;
- `Utente` o `Guest` di un tenant che accede a dati, cookie o preview di un altro tenant: negato.

## Migrazione futura

Un eventuale database separato per tenant richiederà un astratto di risoluzione storage e una strategia di migrazione verificata. Non implementare oggi routing multi-database senza un requisito reale.

# ADR-0011 — Identity, ruoli e autorizzazione

- **Stato:** Accepted (rimangono aperti dettagli elencati in `docs/open-questions.md`)
- **Data:** 2026-10-09
- **Aggiornato:** 2026-10-09 (ruolo `Utente` = acquirente registrato; `Guest` per tenant; recupero password; 2FA)

## Contesto

Il progetto è un CMS: la maggior parte dei siti è semplice e non prevede registrazione dei visitatori. Un account registrato serve solo all'e-commerce, per emettere ordini. Gli account amministrativi sono creati dagli operatori (ADR-0014).

## Decisione

Usare ASP.NET Core Identity per gli account locali, con tabelle membership esplicite tra account amministrativi e tenant. Centralizzare le policy di autorizzazione nell'application layer.

### Ruoli

| Ruolo | Ambito | Descrizione |
|---|---|---|
| `PlatformAdmin` | Globale (non legato a un tenant) | Gestisce la piattaforma dal menu **Platform** dell'admin: tenant, siti, domini, utenti `TenantAdmin`. Può aprire l'admin di qualunque tenant. Unico ruolo che crea o eleva `TenantAdmin`. |
| `TenantAdmin` | Un tenant (via membership) | Amministra il proprio tenant: siti, impostazioni, tema, contenuti, media e (dal modulo e-commerce) catalogo e ordini. Fa tutto il lavoro sui contenuti: non esistono per ora ruoli intermedi (editor/publisher). |
| `Utente` | Un tenant | Acquirente registrato di un sito con e-commerce. Può solo emettere ordini. Non accede all'admin. Non esiste nei siti senza e-commerce. |
| `Guest` | Un tenant | Visitatore del sito pubblico senza login. |

### Guest per tenant

`Guest` non è un account. È un principal anonimo **vincolato al tenant** risolto dall'host: porta sempre il `TenantId` e non è mai condiviso fra tenant. Ogni tenant ha un proprio profilo Guest (`TenantGuestProfile`), creato insieme al tenant; inizialmente non ha impostazioni. Cookie, token di anteprima e, in futuro, carrello e consensi del Guest sono limitati al suo tenant. Il Guest non può emettere ordini (nessun guest checkout, per ora).

### Utente (acquirente)

- La registrazione è consentita **solo** sui siti con e-commerce attivo (impostazione per sito, disattivata di default). Sugli altri siti non esiste alcuna pagina o endpoint di registrazione.
- La registrazione crea sempre e soltanto un `Utente`: nessun parametro della richiesta può scegliere il ruolo.
- L'`Utente` appartiene a un solo tenant: stessa email su tenant diversi corrisponde ad account distinti (assunzione da confermare).
- Entra dal sito pubblico del tenant (area cliente). Non ha accesso all'admin né alle API amministrative.

### Chi può ordinare

Possono emettere ordini gli account autenticati: `Utente`, `TenantAdmin` e `PlatformAdmin`. Il `Guest` no. L'ordine registra account e tenant dell'acquirente.

### Regole sugli account amministrativi

1. Solo il `PlatformAdmin` può creare un account `TenantAdmin` o elevare un account a `TenantAdmin`.
2. Nessun ruolo può assegnare un ruolo superiore al proprio né modificare il proprio ruolo; il `TenantAdmin` non può creare né elevare `TenantAdmin` o `PlatformAdmin`.
3. La creazione di nuovi `PlatformAdmin` è riservata ai `PlatformAdmin`; il primo viene creato dal comando di bootstrap (ADR-0014).
4. Non esiste registrazione self-service per `PlatformAdmin` e `TenantAdmin`.
5. Ogni operazione amministrativa verifica membership, ruolo e risorsa; un ID noto non basta (anti-IDOR).
6. Il `PlatformAdmin` che entra nell'admin di un tenant opera con un contesto tenant esplicito; ingresso e operazioni sono registrati in `AuditLog`.
7. Le regole 1–4 sono applicate lato server e hanno test automatici di non-escalation.

### Recupero password

Vale per tutti gli account con login. Flusso:

1. L'utente richiede il recupero indicando l'email. La risposta è sempre la stessa, esista o no l'account (nessuna enumerazione).
2. Se l'account esiste, parte una email con un **codice OTP** di sicurezza a scadenza breve, con tentativi limitati e richieste soggette a rate limiting. Il codice è conservato solo come hash.
3. Inserito l'OTP corretto, il server invia una email con un **link monouso** a scadenza breve per impostare la nuova password. Il link viene invalidato all'uso e da ogni nuova richiesta di recupero.
4. Cambiata la password, le sessioni e i token esistenti dell'account sono invalidati e l'utente riceve una notifica email.

Le email passano dall'outbox (ADR-0010). Serve un provider email (questione aperta).

### Two-factor authentication

È un requisito previsto ma **non prioritario**: va implementato per ultimo, dopo le altre funzionalità. Quando verrà affrontato servirà un ADR (metodo, recupero, obbligo per `PlatformAdmin` e `TenantAdmin`).

## Alternative considerate

- Ruoli granulari (Editor, Publisher, StoreManager, SiteAdmin) e permessi per membership: rimandati. Oggi tutto il lavoro sui contenuti è del `TenantAdmin`; si potranno aggiungere ruoli intermedi con un nuovo ADR.
- Guest come ruolo unico globale: respinto, deve essere sempre legato a un tenant.
- Guest checkout: escluso per ora.
- Identity provider esterno obbligatorio dal day one: rimandato finché i requisiti SSO non sono chiari.

## Conseguenze

- Per M1–M6 servono solo `PlatformAdmin`, `TenantAdmin` e il principal `Guest`; `Utente` e registrazione arrivano con l'e-commerce (M7).
- Serve un provider email già per il recupero password degli account admin.
- Gli account `Utente` hanno `TenantId`; gli account amministrativi sono globali con membership.

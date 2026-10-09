# ADR-0011 — Identity, ruoli e autorizzazione

- **Stato:** Accepted (rimangono aperti dettagli elencati in `docs/open-questions.md`)
- **Data:** 2026-10-09
- **Aggiornato:** 2026-10-09 (ruolo `Utente` = acquirente registrato; `Guest` per tenant; recupero password; 2FA; ambito di accesso di `TenantAdmin` e `PlatformAdmin`; account sempre legati a un solo tenant)

## Contesto

Il progetto è un CMS: la maggior parte dei siti è semplice e non prevede registrazione dei visitatori. Un account registrato serve solo all'e-commerce, per emettere ordini. Gli account amministrativi sono creati dagli operatori (ADR-0014).

## Decisione

Usare ASP.NET Core Identity per gli account locali. Ogni account, tranne il `PlatformAdmin`, appartiene a **un solo tenant** (`TenantId` dell'account). Centralizzare le policy di autorizzazione nell'application layer.

### Appartenenza ai tenant e unicità dell'email

- Un tenant può avere **più `TenantAdmin`** (ad esempio due persone che gestiscono lo stesso sito).
- Un account `TenantAdmin` o `Utente` appartiene a un solo tenant.
- L'email è unica **all'interno di un tenant** e può essere ripetuta in tenant diversi: sono account distinti, con credenziali e password distinte. Vale per `TenantAdmin` e `Utente`.
- Il `PlatformAdmin` è globale (senza `TenantId`); la sua email è unica fra i `PlatformAdmin`.
- Conseguenza: ovunque si identifichi un account (login, recupero password, registrazione) serve il tenant oltre all'email. L'unicità di nome utente/email di ASP.NET Core Identity va configurata per tenant, non globale.

### Ruoli

| Ruolo | Ambito | Descrizione |
|---|---|---|
| `PlatformAdmin` | Globale (non legato a un tenant) | Entra nell'admin di qualunque tenant e lì lavora **come un `TenantAdmin`** di quel tenant, con in più il menu **Platform** per gestire tenant, siti, domini e utenti `TenantAdmin`. Unico ruolo che crea o eleva `TenantAdmin`. |
| `TenantAdmin` | Un solo tenant | Accede solo al proprio tenant; un tenant può averne più di uno. Amministra il proprio tenant: siti, impostazioni, tema, contenuti, media e (dal modulo e-commerce) catalogo e ordini. Fa tutto il lavoro sui contenuti: non esistono per ora ruoli intermedi (editor/publisher). |
| `Utente` | Un tenant | Acquirente registrato di un sito con e-commerce. Può solo emettere ordini. Non accede all'admin. Non esiste nei siti senza e-commerce. |
| `Guest` | Un tenant | Visitatore del sito pubblico senza login; vede tutto ciò che è pubblico. |

### Guest per tenant

`Guest` non è un account. È un principal anonimo **vincolato al tenant** risolto dall'host: porta sempre il `TenantId` e non è mai condiviso fra tenant. Ogni tenant ha un proprio profilo Guest (`TenantGuestProfile`), creato insieme al tenant, senza impostazioni: il Guest vede tutto il contenuto pubblicato. Limitare la visibilità è un'evoluzione futura, non prevista ora. Cookie, token di anteprima e, in futuro, carrello e consensi del Guest sono limitati al suo tenant. Il Guest non può emettere ordini (nessun guest checkout, per ora).

### Utente (acquirente)

- La registrazione è consentita **solo** sui siti con e-commerce attivo (impostazione per sito, disattivata di default). Sugli altri siti non esiste alcuna pagina o endpoint di registrazione.
- La registrazione crea sempre e soltanto un `Utente`: nessun parametro della richiesta può scegliere il ruolo.
- L'`Utente` appartiene a un solo tenant: la stessa email su tenant diversi corrisponde ad account distinti.
- Entra dal sito pubblico del tenant (area cliente). Non ha accesso all'admin né alle API amministrative.

### Chi può ordinare

Possono emettere ordini gli account autenticati: `Utente`, `TenantAdmin` e `PlatformAdmin`. Il `Guest` no. Un `TenantAdmin` ordina solo nel proprio tenant; un `PlatformAdmin` in qualunque tenant. Usano le stesse credenziali dell'admin per autenticarsi sul sito pubblico, ma sul sito pubblico hanno solo capacità di acquirente. L'ordine registra account e tenant dell'acquirente.

### Regole sugli account amministrativi

1. Solo il `PlatformAdmin` può creare un account `TenantAdmin` o elevare un account a `TenantAdmin`.
2. Nessun ruolo può assegnare un ruolo superiore al proprio né modificare il proprio ruolo; il `TenantAdmin` non può creare né elevare `TenantAdmin` o `PlatformAdmin`.
3. La creazione di nuovi `PlatformAdmin` è riservata ai `PlatformAdmin`; il primo viene creato dal comando di bootstrap (ADR-0014).
4. Non esiste registrazione self-service per `PlatformAdmin` e `TenantAdmin`.
5. Ogni operazione amministrativa verifica tenant dell'account, ruolo e risorsa; un ID noto non basta (anti-IDOR).
6. Il `PlatformAdmin` che entra nell'admin di un tenant opera con un contesto tenant esplicito e con gli stessi poteri di un `TenantAdmin` di quel tenant, più il menu Platform. L'ingresso e le operazioni di piattaforma sono registrati in `AuditLog` con attore e data; non è richiesto inserire un motivo. Non è prevista l'impersonation di altri utenti.
7. Un `TenantAdmin` non può accedere ad altri tenant né all'area Platform.
8. Le regole 1–4 e 7 sono applicate lato server e hanno test automatici di non-escalation.

### Recupero password

Vale per tutti gli account con login. Flusso:

1. L'utente richiede il recupero indicando l'email. La risposta è sempre la stessa, esista o no l'account (nessuna enumerazione).
2. Se l'account esiste, parte una **prima email** con un **codice OTP** di sicurezza a scadenza breve, con tentativi limitati e richieste soggette a rate limiting. Il codice è conservato solo come hash.
3. Inserito l'OTP corretto, il server invia una **seconda email, distinta**, con un **link monouso** a scadenza breve per impostare la nuova password. Il link viene invalidato all'uso e da ogni nuova richiesta di recupero. Il link non viene mai mostrato a schermo né inviato insieme al codice.
4. Cambiata la password, le sessioni e i token esistenti dell'account sono invalidati e l'utente riceve una notifica email.

Valori iniziali proposti, da confermare al bootstrap e rendere configurabili: OTP numerico di 6 cifre valido 10 minuti con massimo 5 tentativi; link valido 30 minuti; massimo 3 richieste di recupero per ora per account e per IP.

Le email passano dall'outbox (ADR-0010) e dagli adapter di ADR-0015 (Microsoft Graph, Gmail, SendGrid, SMTP).

### Two-factor authentication

È un requisito previsto ma **non prioritario**: va implementato per ultimo, dopo le altre funzionalità, e **non è un gate del go-live**. Quando verrà affrontato servirà un ADR (metodo, recupero, obbligo per `PlatformAdmin` e `TenantAdmin`).

## Alternative considerate

- Ruoli granulari (Editor, Publisher, StoreManager, SiteAdmin) e permessi: rimandati. Oggi tutto il lavoro sui contenuti è del `TenantAdmin`; si potranno aggiungere ruoli intermedi con un nuovo ADR.
- Guest come ruolo unico globale: respinto, deve essere sempre legato a un tenant.
- Guest checkout: escluso per ora.
- Identity provider esterno obbligatorio dal day one: rimandato finché i requisiti SSO non sono chiari.

## Conseguenze

- Per M1–M6 servono solo `PlatformAdmin`, `TenantAdmin` e il principal `Guest`; `Utente` e registrazione arrivano con l'e-commerce (M7).
- Servono gli adapter email (ADR-0015) già per il recupero password degli account admin.
- Tutti gli account tranne il `PlatformAdmin` hanno `TenantId`; il `PlatformAdmin` accede a tutti i tenant.
- Il login e il recupero password devono sapere a quale tenant appartiene l'account (vedi `docs/open-questions.md`).
- Non serve una tabella di membership multi-tenant; verrà introdotta solo se un giorno un account dovrà lavorare su più tenant (nuovo ADR).

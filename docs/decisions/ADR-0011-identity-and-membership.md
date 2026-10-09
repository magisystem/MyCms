# ADR-0011 — Identity, ruoli e autorizzazione tramite membership

- **Stato:** Accepted (rimangono aperte le policy password/MFA, vedi `docs/open-questions.md`)
- **Data:** 2026-10-09

## Contesto

Un utente può lavorare su tenant e siti multipli con ruoli differenti. Il ruolo deve essere collegato al contesto e alla risorsa, non soltanto a un claim globale. I clienti non si registrano da soli (ADR-0014).

## Decisione

Partire da ASP.NET Core Identity per gli account locali, con tabelle membership esplicite tra utente e tenant/sito. Centralizzare policy autorizzative nell'application layer.

### Ruoli

| Ruolo | Ambito | Descrizione |
|---|---|---|
| `PlatformAdmin` | Globale (non legato a un tenant) | Gestisce la piattaforma: tenant, siti, domini, utenti `TenantAdmin`. Unico ruolo che può creare o elevare `TenantAdmin`. |
| `TenantAdmin` | Un tenant (via membership) | Amministra il proprio tenant: siti, impostazioni, contenuti e utenti `Utente` del tenant. |
| `Utente` | Un tenant, opzionalmente ristretto a siti (via membership) | Lavora sui contenuti secondo i permessi assegnati dal `TenantAdmin`. |
| `Guest` | Nessuno | Visitatore non autenticato del sito pubblico. Non ha account né accesso all'admin; vede le preview solo con token a scadenza breve. |

`PlatformAdmin` è un attributo globale dell'account e non una membership. `TenantAdmin` e `Utente` sono ruoli della membership: lo stesso account può avere ruoli diversi in tenant diversi.

### Permessi dell'Utente

I compiti di editor, publisher e store manager sono **permessi** assegnati alla membership di un `Utente`, non ruoli distinti. Insieme iniziale (da raffinare nel bootstrap):

- `Content.Edit` — crea e modifica bozze di pagine.
- `Content.Publish` — pubblica, ripristina revisioni.
- `Media.Manage` — carica e gestisce i media.
- `Store.Manage` — gestisce catalogo, inventario e ordini (dopo M7).

Il `TenantAdmin` dispone implicitamente di tutti i permessi sul proprio tenant.

### Regole sugli utenti

1. Solo il `PlatformAdmin` può creare un account `TenantAdmin` o elevare un account a `TenantAdmin`.
2. Il `TenantAdmin` può creare e modificare account `Utente` nel proprio tenant e assegnare permessi e siti, ma non può creare né elevare `TenantAdmin` o `PlatformAdmin`, né modificare il proprio ruolo.
3. Nessun ruolo può assegnare un ruolo superiore al proprio né modificare il proprio ruolo.
4. La creazione di nuovi `PlatformAdmin` è riservata ai `PlatformAdmin`; il primo viene creato dal comando di bootstrap (ADR-0014).
5. Non esiste registrazione self-service.
6. Ogni operazione amministrativa verifica membership, ruolo e permessi rispetto alla risorsa; un ID noto non basta (anti-IDOR).
7. L'accesso cross-tenant del `PlatformAdmin` ai dati dei tenant passa da casi d'uso dedicati, con motivo e attore in `AuditLog`.
8. Le regole 1–4 sono applicate lato server e hanno test automatici di non-escalation.

## Alternative considerate

- Ruolo globale sull'utente per tutti i tenant: rifiutato perché non esprime appartenenze differenti.
- Ruoli granulari (Editor, Publisher, StoreManager, SiteAdmin) come ruoli distinti: sostituiti da quattro ruoli più permessi, più semplici da spiegare e amministrare.
- Identity provider esterno obbligatorio dal day one: rimandato finché requisiti SSO non sono chiari.

## Conseguenze

- Le operazioni amministrative devono verificare membership + permessi della risorsa.
- Serve un modello dati per permessi per membership e per l'eventuale restrizione a siti (`SiteMembership`).
- Gli account dei clienti finali dello store (acquirenti) non sono coperti da questo ADR: sono una questione aperta separata.

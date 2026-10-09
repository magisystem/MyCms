# Decisioni aperte

Le scelte sotto non sono definite completamente. Gli agenti devono evitare di inventarle e segnalare quando un'implementazione dipende da una risposta.

## Prodotto

- Piani commerciali, limiti per tenant, quote e billing SaaS.
- Locales supportate all'MVP e comportamento multilingua.
- Requisiti quantitativi di carico, disponibilità e retention.

## Decisioni prese (2026-10-09)

- SQL Server è l'unico database (ADR-0012).
- Nome progetto **DvCms**: solution `DvCms.sln`, namespace e sotto-progetti `DvCms.*`.
- Ruoli `PlatformAdmin`, `TenantAdmin`, `Utente` (acquirente), `Guest` per tenant (ADR-0011). Il `TenantAdmin` svolge tutto il lavoro sui contenuti; nessun ruolo editor/publisher per ora.
- Registrazione solo per gli acquirenti dei siti con e-commerce; nessun guest checkout; possono ordinare `Utente`, `TenantAdmin` e `PlatformAdmin`.
- Recupero password: prima email con codice OTP, seconda email con link monouso (ADR-0011). Two-factor authentication prevista, per ultima, non bloccante per il go-live.
- Email: adapter Microsoft Graph, Gmail API, SendGrid e SMTP dietro un contratto unico (ADR-0015).
- `TenantAdmin` accede solo al proprio tenant; `PlatformAdmin` entra ovunque con i poteri di un `TenantAdmin` più il menu Platform. Un `TenantAdmin` ordina solo nel proprio tenant, il `PlatformAdmin` ovunque.
- `Guest` per tenant senza impostazioni: vede tutto ciò che è pubblico; limitare la visibilità è un'evoluzione futura.
- Duplicati fra `admin-theme/` e `wwwroot` accettati; li ripulirà il committente.
- `admin-theme/`: licenze possedute, repository privato; `tabler` e `tabler-icons` copiati per intero in `wwwroot`.
- Tenant, utenti `TenantAdmin`, domini e certificati gestiti manualmente dal `PlatformAdmin`/operatori (ADR-0014).
- Admin con Tabler e Tabler Icons (ADR-0013).

## Identity

- Solo account locali o SSO/OIDC richiesto per clienti enterprise?
- Policy password (lunghezza, complessità, blocco account) e durate/limiti del recupero password: i valori iniziali proposti sono in ADR-0011 e vanno confermati.
- Account `Utente`: appartenenza a un solo tenant (stessa email su due tenant = due account): da confermare.
- Un `TenantAdmin` ha un solo tenant (dedotto dalla risposta "accede solo al proprio tenant"): da confermare. Se serve gestire più tenant con lo stesso account, va rivisto.
- Il `PlatformAdmin` entra con il proprio account e l'ingresso è auditato (ADR-0014): serve anche un motivo/approvazione o un'impersonation di altri utenti?

## Email

- Ordine di implementazione degli adapter oltre a SMTP (Graph, Gmail, SendGrid) e quale servirà per primo in produzione.
- L'invio è configurato una sola volta per l'installazione o può avere mittente e canale diversi per tenant?
- Indirizzo e dominio mittente (con SPF/DKIM/DMARC).

## Dati e hosting

- Hosting cloud e regione/residenza dati.
- Hosting/edition di SQL Server e relative strategie backup/restore. Il database è già definito (SQL Server, ADR-0012).
- Object storage provider e CDN.
- RPO/RTO, retention backup e obiettivi SLA.

## UI

- HTMX da aggiungere solo in base a una schermata/requisito preciso.
- Design system e requisiti accessibilità dettagliati.

## Page builder

- Spike tecnico deve dimostrare mappatura GrapesJS JSON ↔ documento CMS senza perdita per i componenti supportati.
- Licenza e processo aggiornamento GrapesJS/plugin.
- Esatta lista di componenti, stili liberi ammessi, nesting e breakpoint.
- Scope dei rich text HTML supportati e policy sanitizer.
- Il libero editor deve rimanere composto da componenti CMS noti; codice custom arbitrario è fuori scope.

## Temi

- Strategia/versioning dei pacchetti tema e pipeline release.
- Come si attivano/aggiornano temi per tenant e come si verifica compatibilità contenuti.
- Se temi separati saranno distribuiti tutti nello stesso processo o in deployment distinti in una fase futura.

## Commerce (bloccante prima del checkout produzione)

- Provider pagamenti e checkout flow.
- Mercati e valute.
- Imposte/IVA, fatturazione e responsabilità per il calcolo.
- Spedizioni, resi e rimborsi.
- Prenotazione, rilascio e riconciliazione inventario.
- Retention e dati personali degli account acquirente. (Account obbligatorio per ordinare; nessun guest checkout, vedi ADR-0011.)

## Processo

Quando una risposta cambia un ADR, creare/aggiornare ADR e ottenere approvazione prima di implementare parti a valle.

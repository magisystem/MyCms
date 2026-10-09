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
- Il `PlatformAdmin` vede il menu "Platform" nell'admin e può aprire l'admin di qualunque tenant.
- Recupero password con codice OTP e link monouso; two-factor authentication prevista ma per ultima.
- `admin-theme/`: licenze possedute, repository privato; `tabler` e `tabler-icons` copiati per intero in `wwwroot`.
- Tenant, utenti `TenantAdmin`, domini e certificati gestiti manualmente dal `PlatformAdmin`/operatori (ADR-0014).
- Admin con Tabler e Tabler Icons (ADR-0013).

## Identity

- Solo account locali o SSO/OIDC richiesto per clienti enterprise?
- Policy password/MFA (in particolare per `PlatformAdmin` e `TenantAdmin`) e processo di recupero account (serve un provider email?).
- Account `Utente`: confermata l'appartenenza a un solo tenant (stessa email su due tenant = due account)? Un `TenantAdmin` o `PlatformAdmin` che ordina usa le stesse credenziali dell'admin sul sito pubblico? Può ordinare in qualsiasi tenant o solo nei propri?
- Recupero password: il link monouso arriva per email dopo la verifica dell'OTP (come scritto in ADR-0011) o viene mostrato subito dopo l'OTP? Quali durate e limiti di tentativi?
- Provider email e dominio mittente (servono già per il recupero password, M1).
- Two-factor authentication: va resa bloccante per il go-live o può restare successiva? Metodo (TOTP, email, ecc.).
- Il `Guest` per tenant deve avere impostazioni configurabili (es. visibilità contenuti) o basta il principal anonimo vincolato al tenant?
- Il `PlatformAdmin` entra nell'admin di un tenant con il proprio account e ingresso auditato (ADR-0014). Serve anche richiedere un motivo/approvazione o un'impersonation di altri utenti?

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

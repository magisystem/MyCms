# Decisioni aperte

Le scelte sotto non sono definite completamente. Gli agenti devono evitare di inventarle e segnalare quando un'implementazione dipende da una risposta.

## Prodotto

- Nome prodotto e naming namespace/solution.
- Piani commerciali, limiti per tenant, quote e billing SaaS.
- Locales supportate all'MVP e comportamento multilingua.
- Requisiti quantitativi di carico, disponibilità e retention.

## Decisioni prese (2026-10-09)

- SQL Server è l'unico database (ADR-0012).
- Ruoli `PlatformAdmin`, `TenantAdmin`, `Utente`, `Guest` (ADR-0011).
- Tenant, utenti `TenantAdmin`, domini e certificati gestiti manualmente dal `PlatformAdmin`/operatori (ADR-0014).
- Admin con Tabler e Tabler Icons (ADR-0013).

## Identity

- Solo account locali o SSO/OIDC richiesto per clienti enterprise?
- Policy password/MFA (in particolare per `PlatformAdmin` e `TenantAdmin`) e processo di recupero account (serve un provider email?).
- `Guest`: è solo il visitatore anonimo o serve anche un account in sola lettura (es. revisore di preview)?
- Permessi dell'`Utente`: confermare l'insieme iniziale (`Content.Edit`, `Content.Publish`, `Media.Manage`, `Store.Manage`).
- Un `TenantAdmin` può avere più tenant? Può creare altri `TenantAdmin`? (decisione attuale: no, solo `PlatformAdmin`).
- Account degli acquirenti dello store: tenant-scoped e separati dagli account admin?
- Il supporto platform admin richiede approvazione/audit o impersonation controllata?

## Dati e hosting

- Hosting cloud e regione/residenza dati.
- Hosting/edition di SQL Server e relative strategie backup/restore. Il database è già definito (SQL Server, ADR-0012).
- Object storage provider e CDN.
- RPO/RTO, retention backup e obiettivi SLA.

## UI

- Licenze: `tabler-emails` e `tabler-illustrations` sono a pagamento (licenza personale) e `tabler-avatars` non ha licenza visibile. Il repository è privato? Vanno tenuti, rimossi o spostati fuori dal repository?
- Dimensione di `admin-theme/` (centinaia di MB): tenerla nel repository o ridurla ai soli `tabler` e `tabler-icons`?

- ~~Libreria CSS/admin~~: decisa, Tabler + Tabler Icons (ADR-0013).
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
- Guest checkout, account cliente, retention e dati personali.

## Processo

Quando una risposta cambia un ADR, creare/aggiornare ADR e ottenere approvazione prima di implementare parti a valle.

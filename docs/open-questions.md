# Decisioni aperte

Le scelte sotto non sono definite completamente. Gli agenti devono evitare di inventarle e segnalare quando un'implementazione dipende da una risposta.

## Prodotto

- Nome prodotto e naming namespace/solution.
- Piani commerciali, limiti per tenant, quote e billing SaaS.
- Locales supportate all'MVP e comportamento multilingua.
- Requisiti quantitativi di carico, disponibilità e retention.

## Identity

- Solo account locali o SSO/OIDC richiesto per clienti enterprise?
- Policy password/MFA e processo di recupero account.
- Il supporto platform admin richiede approvazione/audit o impersonation controllata?

## Dati e hosting

- Hosting cloud e regione/residenza dati.
- Hosting/edition di SQL Server (primario), target di hosting PostgreSQL per la suite secondaria e relative strategie backup/restore. La scelta del provider primario è già definita.
- Object storage provider e CDN.
- RPO/RTO, retention backup e obiettivi SLA.

## UI

- Libreria CSS/admin (può restare CSS semplice al bootstrap).
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

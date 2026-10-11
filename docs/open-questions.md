# Decisioni aperte

Le scelte sotto non sono definite completamente. Gli agenti devono evitare di inventarle e segnalare quando un'implementazione dipende da una risposta.

## Prodotto

- Piani commerciali, limiti per tenant, quote e billing SaaS.
- Requisiti quantitativi di carico, disponibilità e retention.

## Decisioni prese (2026-10-09)

- SQL Server è l'unico database (ADR-0012).
- Nome progetto **DvCms**: solution `DvCms.sln`, namespace e sotto-progetti `DvCms.*`.
- Ruoli `PlatformAdmin`, `TenantAdmin`, `Utente` (acquirente), `Guest` per tenant (ADR-0011). Il `TenantAdmin` svolge tutto il lavoro sui contenuti; nessun ruolo editor/publisher per ora.
- Registrazione solo per gli acquirenti dei siti con e-commerce; nessun guest checkout; possono ordinare `Utente`, `TenantAdmin` e `PlatformAdmin`.
- Recupero password: prima email con codice OTP, seconda email con link monouso (ADR-0011). Two-factor authentication: TOTP con codici di recupero, obbligatoria per `PlatformAdmin` e `TenantAdmin` (ADR-0019).
- Email: adapter Microsoft Graph, Gmail API, SendGrid e SMTP dietro un contratto unico; provider scelto per tenant dal `PlatformAdmin` con UI dinamica; nessuna priorità fra i canali (ADR-0015).
- Un tenant può avere più `TenantAdmin`; un account `TenantAdmin` o `Utente` appartiene a un solo tenant; la stessa email vale in un tenant e può ripetersi in tenant diversi (ADR-0011).
- Il tenant è deciso solo dall'host dell'URL: un account non può accedere dall'host di un altro tenant (salvo il `PlatformAdmin`), nessun dominio centrale di amministrazione (ADR-0011).
- Il `PlatformAdmin` non ha recupero password né strumenti di reset per ora: reset manuale dell'operatore, da definire in seguito. Il seeding idempotente crea il tenant primario con il suo dominio e il primo `PlatformAdmin` (ADR-0014).
- Percorso riservato dell'admin: `/admin`. Tenant di gestione: `cms.davidemartelli.dev`, con alias `localhost` (qualunque porta) solo in sviluppo. Sviluppo solo HTTP; i certificati in produzione sono dell'hosting tramite reverse proxy.
- Tenant di test in locale: `cms1.davidemartelli.dev` e `cms2.davidemartelli.dev`, raggiungibili solo dal computer locale. Il recupero password vale per `TenantAdmin` e `Utente`.
- Chiave di cifratura dei segreti dei tenant nella configurazione dell'ambiente (non nel repository); il key ring può stare nel database solo se protetto da quella chiave (ADR-0015).
- Limiti del recupero password confermati. Nessun motivo obbligatorio per l'ingresso del `PlatformAdmin` nei tenant, nessuna impersonation.
- `TenantAdmin` accede solo al proprio tenant; `PlatformAdmin` entra ovunque con i poteri di un `TenantAdmin` più il menu Platform. Un `TenantAdmin` ordina solo nel proprio tenant, il `PlatformAdmin` ovunque.
- `Guest` per tenant senza impostazioni: vede tutto ciò che è pubblico; limitare la visibilità è un'evoluzione futura.
- Duplicati fra `admin-theme/` e `wwwroot` accettati; li ripulirà il committente.
- `admin-theme/`: licenze possedute, repository privato; `tabler` e `tabler-icons` copiati per intero in `wwwroot`.
- Account locali propri nella tabella `Utenze`, senza ASP.NET Core Identity, con password BCrypt e blocco di 5 minuti dopo 5 tentativi falliti (ADR-0016). Comando di seeding `DvCms.Web seed`. Tutto in italiano: tabelle, colonne, funzioni, configurazione (ADR-0017); nomi dei progetti in inglese; `Tenant`, `Slug`, i nomi dei ruoli e `seed` restano. Lingue italiano (`it`, lingua di base e predefinita) e inglese (`en`, cultura `en-GB`); lingua dell'admin dalla preferenza dell'account, del sito pubblico dal prefisso del percorso (`/en/...`) o, senza prefisso, dalla lingua predefinita del sito; pagina inesistente nella lingua richiesta = 404; cache dei testi con invalidazione solo locale per ora; testi statici con `@_L["TESTO"]` nel database, modificabili dal `PlatformAdmin` (ADR-0018). Seeding idempotente del tenant primario e del primo `PlatformAdmin` da configurazione e segreti (ADR-0014).
- Tenant, utenti `TenantAdmin` e domini gestiti manualmente dal `PlatformAdmin`; certificati TLS dell'hosting tramite reverse proxy (ADR-0014).
- Admin con Tabler e Tabler Icons (ADR-0013).

## Account e autenticazione

- Solo account locali o SSO/OIDC richiesto per clienti enterprise?
- Lunghezza massima della password e policy password (lunghezza minima, complessità). I limiti del recupero password sono quelli di ADR-0011.

## Da confermare per M0/M1 (vedi `docs/delivery/plan-m0-m1.md`)

- Pulizia delle chiavi di testo non più usate dai testi statici (non prioritaria).


- Framework di test: xUnit (proposta).
- Ruolo come colonna della tabella `Utenze` (quattro ruoli fissi, nessuna tabella di ruoli) e seeding dei tenant di test solo in `Development` (proposte).
- Verifica di licenza e manutenzione di `BCrypt.Net-Next` prima di aggiungerlo (regola 11).
- Credenziali iniziali del `TenantAdmin` in M1: password temporanea mostrata una volta, cambio obbligatorio al primo accesso; invito via email in M1b, ora disponibile se il tenant ha il canale email configurato.
- Email di un `TenantAdmin` o `Utente` uguale a quella di un `PlatformAdmin`: rifiutata alla creazione (proposta).
- SQL Server in CI: service container; in locale istanza o container con connection string da environment variable (proposta).
- Disponibilità del .NET 10 SDK negli ambienti di sviluppo e CI.

## Email

- Indirizzo e dominio mittente di ogni tenant (con SPF/DKIM/DMARC): attività manuale del `PlatformAdmin`.

## Dati e hosting

- Hosting cloud e regione/residenza dati.
- Hosting/edition di SQL Server e relative strategie backup/restore. Il database è già definito (SQL Server, ADR-0012).
- Object storage provider e CDN.
- RPO/RTO, retention backup e obiettivi SLA. Proposta in `docs/operations/backup-e-ripristino.md` (RPO 15 minuti per il database e 24 ore per i file, RTO 4 ore, retention 35 giorni): da confermare.

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

## Page builder (dopo il PoC M4)

- Rich text nel builder: risolto con un markup a grammatica chiusa senza sanitizzazione HTML (ADR-0005). Aperto: se servono formattazioni più ricche (titoli interni, tabelle, immagini in linea), estendere la grammatica con vettori di prova o usare componenti dedicati.
- Accessibilità dell'editor: attuato l'uso da tastiera (vedi `page-builder.md`). Aperto: prova con uno screen reader reale, audit WCAG automatico (nuova dipendenza), contrasti, spostamento di un componente esistente in un altro contenitore da tastiera.
- Migrazione dei documenti tra versioni di template e di schema (oggi il passaggio a una nuova versione è rifiutato per le pagine del builder).
- Collegamento del testo alternativo delle immagini del builder con quelli per lingua della libreria media (ADR-0018).
- `backbone-undo` (transitiva di GrapesJS) non più supportata: rivalutare prima di M5.


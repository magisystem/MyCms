# Roadmap incrementale

La roadmap è ordinata per dipendenze. Le stime vanno fatte solo dopo un primo esame del repository e non sono incluse qui.

## M0 — Bootstrap e decisioni

- Creare solution .NET 10, configurare nullable, formatter/analyzer e CI.
- Decidere framework test, convenzioni ID, logging, gestione segreti e hosting target iniziale.
- Configurare SQL Server ed EF Core con una serie di migrazioni e il setup locale.
- Localizzatore dei testi statici (`@_L["TESTO"]`, tabelle `Lingue` e `TestiStatici`, italiano e inglese, ADR-0018): i testi del layout admin passano da subito dal localizzatore.
- Preparare il layout admin Razor con Tabler e Tabler Icons (solo gli asset usati in `wwwroot`).
- Creare i primi ADR specifici se si prendono decisioni nuove.

**Exit criteria:** build pulita, test vuoto eseguibile in CI, SQL Server configurabile, migrazione iniziale applicabile da zero, layout admin Tabler renderizzato e setup locale documentato.

## M1 — Vertical slice tenant/sito/pagina

- Tenant, site e domain registry.
- Ruoli `PlatformAdmin` e `TenantAdmin`, principal `Guest` per tenant, account locali propri (ADR-0016) e seeding idempotente del tenant primario e del primo `PlatformAdmin`.
- Schermata "Testi" del menu Platform per modificare i testi statici (B-010).
- Menu "Platform" nell'admin (solo `PlatformAdmin`): crea tenant, sito, dominio (verificato a mano) e utente `TenantAdmin`; il `PlatformAdmin` può aprire l'admin di qualunque tenant (auditato).
- Context tenant derivato dal dominio e dal tenant dell'account admin.
- Pagina semplice draft/published.
- Rendering Razor per pagina pubblicata.
- Test d'isolamento cross-tenant.

Il piano dettagliato è in [`plan-m0-m1.md`](plan-m0-m1.md).

**Exit criteria:** un `PlatformAdmin` crea due tenant con i rispettivi `TenantAdmin`; i due tenant pubblicano lo stesso slug su siti/domìni distinti; nessun accesso incrociato; tentativi di escalation di ruolo respinti.

## M1b — Email e recupero password

- Contratto email con adapter SMTP (A-005) e configurazione email del tenant (B-009).
- Outbox minimo (tabella e worker semplice, C-007).
- Recupero password per `TenantAdmin` e `Utente` con codice OTP e link monouso in due email distinte (B-008); invito via email per i nuovi `TenantAdmin`.
- Gli adapter Graph, Gmail e SendGrid (A-006, A-007, A-008) sono completati.

**Exit criteria:** recupero password completo in due email con SMTP, limiti e invalidazioni verificati da test; segreti del tenant cifrati; nessun invio dalla richiesta web.

## M2 — CMS guidato e revisioni

- CRUD pagine e slug.
- Pagine in più lingue (C-008): una pagina per lingua, `hreflang`, selettore di lingua.
- Template guidati iniziali e form Razor.
- Revisioni immutabili, preview sicura, concorrenza optimistic.
- Publish e invalidazione cache sull'outbox minimo introdotto in M1b; la versione production-ready resta in M6.
- SEO base, redirects e invalidazione cache.

**Exit criteria:** creare/modificare/preview/publish/revert una pagina e verificare HTML pubblico.

## M3 — Temi Razor e media

- Registry temi, contratto settings, primo tema completo.
- Storage asset con ownership tenant/site.
- Libreria media e validazione upload.
- Secondo tema di prova per verificare che contenuti e presentazione siano separati.

**Exit criteria:** stessa struttura compatibile visualizzata con due temi senza duplicare il contenuto.

## M4 — Proof of concept page builder

- Integrare GrapesJS in una Razor Page dedicata.
- Componenti layout/contenuto minimi.
- Persistenza EditorProjectJson.
- Adapter verso RenderDocumentJson.
- Locking template e slot consentiti lato server.
- Test round-trip, schema version e XSS policy.

**Exit criteria:** builder valutato con criteri espliciti; se conversione non è affidabile, aggiornare ADR prima di estendere il catalogo.

**Stato:** completato il 2026-10-10. Valutazione in [`valutazione-poc-m4.md`](valutazione-poc-m4.md): conversione affidabile per il catalogo di base, ADR-0005 e ADR-0007 confermati (*Accepted*) con precisazioni. Si può procedere con M5.

## M5 — Page builder MVP

- Desktop/tablet/mobile preview.
- Inline edit e pannello proprietà.
- Autosave controllato, revisioni e preview.
- Componenti avanzati selezionati e animazioni declarative.
- Drag/reorder/duplicate/delete soggetti a policy.

**Exit criteria:** user journey template guidato e pagina libera completati, validati e renderizzati server-side.

**Stato (2026-10-10):** in corso. Fatti: anteprima per dispositivo, modifica del testo nel canvas, salvataggio automatico controllato, componenti avanzati (hero, invito all'azione, fisarmonica) e animazioni dichiarative, template v2 con passaggio sicuro. Fatto anche il testo formattato (markup a grammatica chiusa, senza sanitizzazione HTML) e il contratto componenti-temi (token `--dv-*` e skin per tema, [`componenti-e-temi.md`](../architecture/componenti-e-temi.md)). Da fare: pannello proprietà avanzato, componenti ulteriori (griglia di caratteristiche, testimonianze), user journey completo da validare. Dettagli in `docs/architecture/page-builder.md`.

## M6 — SEO e operatività production-ready

- Sitemap e robots per dominio.
- Output cache e invalidazione.
- Audit, metrics, health checks, backup e restore.
- Test sicurezza, performance baseline e runbook.

## M7 — E-commerce MVP

- Catalogo e varianti, con traduzioni per lingua (F-006).
- Prezzi/valuta, carrello e checkout.
- Registrazione e login degli acquirenti (`Utente`), abilitati per sito; ordini riservati ad account autenticati.
- Provider di pagamento approvato e webhook idempotenti.
- Ordini, snapshot, inventario e email essenziali.
- Componenti commerce nel renderer/page builder.

**Exit criteria:** test integrato per pagamento riuscito/fallito/duplicato, tenancy e riconciliazione; policy fiscali/spedizione approvate.

## M8 — Lancio controllato

- Pilot con pochi tenant.
- Runbook supporto e procedure recupero.
- Audit di sicurezza, privacy e performance.
- Migrazione/release checklist e strategia rollback.

Non iniziare M5 prima che il proof of concept M4 convalidi il modello documento/renderer.

## Ultima priorità — Two-factor authentication

Completata (G-006, [ADR-0019](../decisions/ADR-0019-two-factor-authentication.md)): TOTP con codici di recupero, obbligatoria per `PlatformAdmin` e `TenantAdmin` per impostazione predefinita. Il recupero password via OTP e link monouso è in M1.

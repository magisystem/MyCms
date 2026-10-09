# Roadmap incrementale

La roadmap è ordinata per dipendenze. Le stime vanno fatte solo dopo un primo esame del repository e non sono incluse qui.

## M0 — Bootstrap e decisioni

- Creare solution .NET 10, configurare nullable, formatter/analyzer e CI.
- Decidere framework test, convenzioni ID, logging, gestione segreti e hosting target iniziale.
- Configurare SQL Server ed EF Core con una serie di migrazioni e il setup locale.
- Preparare il layout admin Razor con Tabler e Tabler Icons (solo gli asset usati in `wwwroot`).
- Creare i primi ADR specifici se si prendono decisioni nuove.

**Exit criteria:** build pulita, test vuoto eseguibile in CI, SQL Server configurabile, migrazione iniziale applicabile da zero, layout admin Tabler renderizzato e setup locale documentato.

## M1 — Vertical slice tenant/sito/pagina

- Tenant, site e domain registry.
- Ruoli `PlatformAdmin` e `TenantAdmin`, principal `Guest` per tenant e comando di bootstrap del primo `PlatformAdmin`.
- Menu "Platform" nell'admin (solo `PlatformAdmin`): crea tenant, sito, dominio (verificato a mano) e utente `TenantAdmin`; il `PlatformAdmin` può aprire l'admin di qualunque tenant (auditato).
- Recupero password con codice OTP e link monouso (richiede un provider email e l'outbox minimo).
- Context tenant derivato dal dominio e membership admin.
- Pagina semplice draft/published.
- Rendering Razor per pagina pubblicata.
- Test d'isolamento cross-tenant.

**Exit criteria:** un `PlatformAdmin` crea due tenant con i rispettivi `TenantAdmin`; i due tenant pubblicano lo stesso slug su siti/domìni distinti; nessun accesso incrociato; tentativi di escalation di ruolo respinti.

## M2 — CMS guidato e revisioni

- CRUD pagine e slug.
- Template guidati iniziali e form Razor.
- Revisioni immutabili, preview sicura, concorrenza optimistic.
- Outbox minimo (tabella e worker semplice) per publish e invalidazione cache; la versione production-ready resta in M6.
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

## M5 — Page builder MVP

- Desktop/tablet/mobile preview.
- Inline edit e pannello proprietà.
- Autosave controllato, revisioni e preview.
- Componenti avanzati selezionati e animazioni declarative.
- Drag/reorder/duplicate/delete soggetti a policy.

**Exit criteria:** user journey template guidato e pagina libera completati, validati e renderizzati server-side.

## M6 — SEO e operatività production-ready

- Sitemap e robots per dominio.
- Output cache e invalidazione.
- Audit, metrics, health checks, backup e restore.
- Test sicurezza, performance baseline e runbook.

## M7 — E-commerce MVP

- Catalogo e varianti.
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

Requisito previsto ma non prioritario: si affronta dopo il resto (G-006), con un ADR dedicato. Il recupero password via OTP e link monouso è invece in M1.

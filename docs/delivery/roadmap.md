# Roadmap incrementale

La roadmap è ordinata per dipendenze. Le stime vanno fatte solo dopo un primo esame del repository e non sono incluse qui.

## M0 — Bootstrap e decisioni

- Creare solution .NET 10, configurare nullable, formatter/analyzer e CI.
- Decidere framework test, convenzioni ID, logging, gestione segreti e hosting target iniziale.
- Stabilire le due serie di migrazioni EF Core (SQL Server primaria e PostgreSQL secondaria) e il setup locale.
- Creare i primi ADR specifici se si prendono decisioni nuove.

**Exit criteria:** build pulita, test vuoto eseguibile in CI, SQL Server e PostgreSQL configurabili, entrambe le migrazioni iniziali applicabili da zero e setup locale documentato.

## M1 — Vertical slice tenant/sito/pagina

- Tenant, site e domain registry.
- Context tenant derivato dal dominio e membership admin.
- Pagina semplice draft/published.
- Rendering Razor per pagina pubblicata.
- Test d'isolamento cross-tenant.

**Exit criteria:** due tenant possono pubblicare lo stesso slug su due siti/domìni distinti; nessun accesso incrociato.

## M2 — CMS guidato e revisioni

- CRUD pagine e slug.
- Template guidati iniziali e form Razor.
- Revisioni immutabili, preview sicura, concorrenza optimistic.
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

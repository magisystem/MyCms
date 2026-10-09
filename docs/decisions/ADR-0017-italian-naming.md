# ADR-0017 — Denominazione in italiano

- **Stato:** Accepted
- **Data:** 2026-10-09
- **Supera:** la regola "codice e identificatori in inglese" della versione precedente di `AGENTS.md`

## Contesto

Il committente vuole tutto in italiano: nomi delle tabelle, delle colonne, delle funzioni e, più in generale, di tutto ciò che è scritto da noi nel programma.

## Decisione

Sono in italiano:

- tabelle, colonne, indici, chiavi e vincoli del database;
- classi, interfacce, proprietà, metodi e funzioni, variabili, enumerazioni e i loro valori;
- namespace e nomi di progetto (`DvCms.Dominio`, `DvCms.Applicazione`, `DvCms.Infrastruttura`, `DvCms.Negozio`, `DvCms.Temi.*`, `DvCms.ServizioBackground`, `DvCms.TestUnitari`, `DvCms.TestIntegrazione`, `DvCms.TestIsolamentoTenant`; `DvCms.Web` resta);
- chiavi di configurazione e sezioni (`Seed:TenantPrimario`, `ConnectionStrings:Predefinita`, ...), nomi dei file di configurazione scelti da noi e delle cartelle del repository (`frontend/editor-pagine`);
- chiavi e nomi del documento JSON dei contenuti e identificatori dei componenti (`versioneSchema`, `nodi`, `contenuto.hero`, `negozio.griglia-prodotti`);
- commenti, messaggi di log e di errore nel codice, e documentazione.

Restano in inglese, perché non sono nostri:

- API, tipi e metodi del framework e delle librerie (`DbContext`, `ILogger`, `CancellationToken`, `ToTable`, `Guid`, ecc.) e i nomi imposti da convenzioni di ASP.NET (`appsettings.json`, `Program`, `Development`, `Production`, `wwwroot`);
- nomi di prodotti e servizi (`Smtp`, `Graph`, `Gmail`, `SendGrid`, `GrapesJS`, `Tabler`, `BCrypt`);
- i nomi dei ruoli stabiliti dal committente (`PlatformAdmin`, `TenantAdmin`, `Utente`, `Guest`), il termine `Tenant` (`IdTenant`, `ContestoTenant`), `Slug`, e il comando `seed` (`DvCms.Web seed`) già approvato;
- i prefissi dei commit (`feat:`, `fix:`, ...) e termini tecnici universali senza traduzione corrente (`outbox`, `hash`, `token`, `cache`, `log`, `worker`...), che si possono usare come parte di un nome italiano (`MessaggioOutbox`, `HashPassword`).

Il dettaglio sui nomi delle entità e delle tabelle è in `docs/architecture/domain-model.md`. Nel codice la mappatura fra classe e tabella è sempre esplicita.

## Alternative considerate

- Inglese nel codice e italiano nell'interfaccia: la convenzione più diffusa, ma non è ciò che il committente vuole.
- Solo tabelle in italiano: la prima formulazione, ampliata dal committente a tutto.

## Conseguenze

- Il codice mescola italiano (nostro) e inglese (framework): è inevitabile e accettato.
- Non esiste un controllo automatico sulla lingua dei nomi: si verifica in revisione (checklist di `CONTRIBUTING.md`).
- I documenti e gli ADR usano i nomi italiani degli identificatori; i vecchi nomi inglesi non vanno reintrodotti.

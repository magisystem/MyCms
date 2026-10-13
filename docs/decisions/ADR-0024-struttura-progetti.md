# ADR-0024 — Struttura dei progetti: un assembly per responsabilità

- **Stato:** Accepted (richiesta del committente del 2026-10-13, con le risposte alle sei domande del report [`fattibilita-ristrutturazione-progetti.md`](../delivery/fattibilita-ristrutturazione-progetti.md))
- **Data:** 2026-10-13
- **Modifica:** sostituisce la suddivisione `Domain` / `Application` / `Infrastructure` (regola 3b di `AGENTS.md`, [ADR-0001](ADR-0001-modular-monolith.md) nella parte «progetti/assembly multipli») e supera [ADR-0012](ADR-0012-sql-server-only.md) **solo** per l'esistenza di un progetto segnaposto `DvCms.PostgreSql`: il supporto a PostgreSQL resta non implementato.

## Contesto

Tre assembly («calderoni») contengono tutto: `Infrastructure` ha 17.000 righe fra persistenza, servizi, email, pagamenti, accessi. Il committente preferisce progetti piccoli e ben separati, anche a scapito della sintesi, e vuole preparare il terreno per un secondo provider di database e per un sistema di modelli email.

## Decisione

Il nome del progetto resta **DvCms** (nessuna rinomina: cookie, metriche e scopi di protezione dei segreti non cambiano). Progetti e namespace radice:

| Progetto | Contenuto |
|---|---|
| `DvCms.Extender` | metodi di estensione e utility generiche, senza dominio |
| `DvCms.Models` | entità (POCO, senza riferimento a EF Core) con le regole pure che stanno accanto ad esse (transizioni d'ordine, IVA, IBAN…) |
| `DvCms.DTO` | i dati scambiati con i servizi: viste, dati in ingresso, esiti, enumerazioni di contratto. **I DTO si tengono come sono** (decisione del committente) |
| `DvCms.Contracts` | le interfacce dei servizi e dei collaboratori (`IServizio…`, `IProvider…`, `IAdapter…`) |
| `DvCms.Core` | logica **senza database**: tenancy (contesto, normalizzatori), sicurezza (hash password, TOTP, protezione dei segreti), localizzazione, opzioni/impostazioni, schema e validazione del documento del builder, calcoli puri |
| `DvCms.Data` | `ContestoDatabase`, configurazioni EF, filtri di query per tenant, risolutore del tenant, interceptor di invalidazione cache, seed, helper di query condivisi. **Nessun riferimento a un provider** |
| `DvCms.SqlServer` | provider SQL Server: `UseSqlServer`, migrazioni e snapshot, SQL grezzo specifico, rilevamento dei deadlock |
| `DvCms.PostgreSql` | **segnaposto vuoto**: nessun pacchetto, nessun codice. Selezionarlo dà un errore esplicito |
| `DvCms.Emails` | canali di invio (Graph, Gmail, SMTP, SendGrid, OAuth), outbox e suo elaboratore, impostazioni email, modelli delle email |
| `DvCms.Services` | servizi di accessi, piattaforma, manutenzione, localizzazione, cache |
| `DvCms.Services.Cms` | servizi di CMS (pagine, sitemap, builder), media, temi |
| `DvCms.Services.Negozio` | servizi di negozio e pagamenti (client PayPal e Nexi compresi) |
| `DvCms.Web` | interfaccia utente (Razor Pages, middleware, webhook, `Program.cs`) |

**Direzione delle dipendenze** (un progetto dipende solo da quelli a sinistra; nessun ciclo):

```
Extender ← Models ← DTO ← Contracts ← Core ← Data ← Emails
                                              ↑
       Services · Services.Cms · Services.Negozio   (indipendenti fra loro)
                                              ↑
                       SqlServer · PostgreSql ← Web
```

- `Models` non dipende da EF Core né da altro; `DTO` dipende solo da `Models`; `Contracts` solo da `DTO` e `Models`; `Core` non tocca il database.
- I tre progetti `Services*` non si referenziano a vicenda: quel che condividono sta in `Data` (helper di query) o in `Core`.
- `Data` non conosce SQL Server né PostgreSQL. I punti che dipendono dal motore stanno dietro interfacce di `Contracts` implementate in `SqlServer`: prenotazione dei messaggi outbox (oggi `UPDLOCK, READPAST`), rilevamento dei deadlock (oggi codice 1205).
- `Web` è l'unico che conosce tutti i progetti: registra i servizi e sceglie il provider con `Database:Provider` (predefinito `SqlServer`).
- Un test di architettura fa rispettare queste dipendenze.

**Modelli email nel database** (funzione successiva alla ristrutturazione): tabella dei modelli per tenant e lingua con ripiego a cascata, motore **Fluid** (Liquid, licenza MIT, solo i campi esposti, nessun codice eseguibile: coerente con la regola 7), validazione al salvataggio, anteprima. Dettagli nella tappa dedicata.

## Alternative considerate

- Tenere tre assembly: respinto, preferenza del committente.
- Una sola libreria di servizi: respinto, 15.000 righe in un progetto.
- Eliminare i DTO usando le entità: respinto per il rischio di fuga dei campi sensibili e di over-posting (report di fattibilità, §5).
- Rinominare in `MyCms`: respinto (era un refuso del committente); avrebbe reso illeggibili i segreti già cifrati.

## Conseguenze

- Migrazione per tappe, ciascuna con tutte le suite verdi (`Models` ed `Extender`, `DTO` e `Contracts`, `Core`, `Data`, provider, `Emails`, `Services*`, modelli email).
- ADR-0012 resta valido per il supporto: SQL Server è l'unico database implementato; PostgreSQL richiede un ADR dedicato (migrazioni, token di concorrenza, SQL grezzo, test su entrambi i motori).
- Le migrazioni si spostano nell'assembly `DvCms.SqlServer`; gli identificativi delle migrazioni non cambiano, quindi i database esistenti restano validi.

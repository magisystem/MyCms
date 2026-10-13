# Osservabilità: sonde di salute e metriche

Stato: attuato in M6. Codice: `src/DvCms.Web/Salute`, `Middleware/MiddlewareMetriche.cs`, `DvCms.Core/Cache/MetricheCache.cs`, `DvCms.Core/Outbox/MetricheOutbox.cs`. Test: `tests/DvCms.IntegrationTests/Avvio/SaluteEMetricheTest.cs`.

## Sonde

| Endpoint | Significato | Uso |
|---|---|---|
| `GET /health/live` | Il processo risponde. Nessun controllo esterno. | Riavvio automatico del contenitore/servizio. |
| `GET /health/ready` | Database raggiungibile **e con tutte le migrazioni applicate**, archivio degli oggetti accessibile. | Inclusione nel bilanciatore; blocca il traffico a un'istanza avviata prima di `migra`. |

- Rispondono `200` (sano) o `503` (non sano) con un JSON minimo: `{"stato":"sano","controlli":{"database":"sano","archivio":"sano"}}`. Mai messaggi di errore, nomi di host o credenziali (i dettagli stanno nel log, riga `Controllo di prontezza ... non riuscito`); `Cache-Control: no-store`.
- Non passano dalla risoluzione del tenant: funzionano con l'indirizzo dell'istanza e con qualunque `Host` (un host sconosciuto altrove darebbe 404). Restano però raggiungibili da chiunque raggiunga l'istanza: **non esporle su Internet** attraverso il proxy pubblico (limitare `/health/*` alla rete interna del bilanciatore). Contengono solo sano/non sano, ma rivelano lo stato dell'infrastruttura.
- Timeout di 5 secondi per controllo. Il controllo dell'archivio verifica che si possa aprire l'elenco delle chiavi; **non** prova la scrittura né misura lo spazio libero: spazio su disco e permessi vanno monitorati a parte (vedi gli alert qui sotto).

## Metriche

Strumentazione con `System.Diagnostics.Metrics` (nessuna dipendenza aggiuntiva). Si raccolgono con OpenTelemetry (aggiungendo i tre meter all'esportatore scelto) oppure, a mano, con `dotnet-counters monitor --counters DvCms.Web,DvCms.Cache,DvCms.Outbox`. Nessun tag contiene percorsi, host, tenant, utenti o contenuti.

| Meter | Strumento | Tag | Note |
|---|---|---|---|
| `DvCms.Web` | `dvcms.http.richieste` (contatore) | `area`, `classe`, `metodo` | `area`: `admin`, `media`, `seo`, `statico`, `salute`, `pubblico`; `classe`: `2xx`…`5xx`; `metodo`: i metodi noti, altrimenti `altro`. |
| `DvCms.Web` | `dvcms.http.durata` (istogramma, ms) | come sopra | Dal middleware delle metriche (dopo l'identificatore di correlazione) alla fine della risposta. |
| `DvCms.Cache` | `dvcms.cache.accessi` | `esito`: `trovato`, `mancato`, `disabilitata` | Rapporto trovati/(trovati+mancati) = efficacia della cache dei contenuti pubblici. |
| `DvCms.Cache` | `dvcms.cache.invalidazioni` | — | Una per ogni invalidazione di un sito (pubblicazione, tema, ecc.). |
| `DvCms.Outbox` | `dvcms.outbox.elaborati`, `dvcms.manutenzione.righe_eliminate` | `tipo`, `esito` / `tabella` | Già descritte in `docs/architecture/operations.md`. |

Non ci sono ancora metriche di query o di checkout (le seconde arriveranno con M7) né un esportatore predefinito: la scelta del backend (Prometheus, Application Insights, ecc.) è una decisione di esercizio non ancora presa.

## Alert suggeriti (da configurare nel sistema di monitoraggio scelto)

- `/health/ready` non sano per più di 2 minuti su un'istanza → non riceve traffico; se persiste per tutte, incidente.
- `dvcms.http.richieste` con `classe=5xx` oltre una soglia percentuale (es. 1% su 5 minuti) o `dvcms.http.durata` p95 oltre il doppio della baseline (`docs/operations/prestazioni.md`).
- Messaggi dell'outbox falliti o in coda da molto (pagina `/admin/platform/coda`; metrica `dvcms.outbox.elaborati` con `esito=fallito`).
- Spazio libero sul volume dell'archivio e dei backup; ultimo backup riuscito più vecchio dell'RPO (`docs/operations/backup-e-ripristino.md`).
- Salto improvviso di `dvcms.cache.accessi` `mancato` rispetto a `trovato` senza una pubblicazione: possibile invalidazione ripetuta.

## Correlazione

Ogni richiesta ha un `X-Correlation-Id` (generato o ricevuto se ha formato sicuro), restituito nella risposta e presente come `IdCorrelazione` in ogni riga di log della richiesta, insieme all'identificatore del tenant risolto. Per un'indagine si parte dall'identificatore mostrato all'utente o letto dall'intestazione.

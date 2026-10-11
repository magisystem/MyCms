# Baseline di prestazioni

Stato: misurata una prima volta l'11 ottobre 2026. Serve a **confrontare lo stesso ambiente prima e dopo una modifica** e a fissare un ordine di grandezza; non è un test di carico, né un criterio di accettazione, né una stima della capacità di produzione.

## Come si misura

```bash
node tools/baseline-prestazioni.mjs http://localhost:5080 /<slug-pagina-pubblicata> /sitemap.xml /robots.txt /css/componenti.css /health/ready --richieste=400
```

Lo script (solo Node 22, nessuna dipendenza) fa un riscaldamento, poi `--richieste` richieste in sequenza e con concorrenza fissa (`--concorrenza`, predefinita 8) per ogni percorso, e stampa richieste/s, p50, p95, p99, massimo, byte e codici di stato. Un'istanza in esecuzione e una pagina pubblicata sono prerequisiti; per averne una si può usare `frontend/page-builder/smoke/percorso-utente.mjs`. Evitare di misurare `/admin/accesso` e simili: il limitatore di richieste risponde 429, che è il comportamento voluto ma non una misura di prestazioni.

## Risultato di riferimento

Ambiente: sviluppo locale, **tutto sulla stessa macchina** (4 vCPU Xeon 2,8 GHz, applicazione in `Release`, `ASPNETCORE_ENVIRONMENT=Development`, SQL Server 2022 in un contenitore sullo stesso host, cache dei contenuti attiva con i valori predefiniti, client Node sullo stesso host che contende la CPU). 400 richieste per riga dopo 20 di riscaldamento. Tempi in millisecondi.

| Percorso | Modo | req/s | p50 | p95 | p99 | max | byte |
|---|---|---:|---:|---:|---:|---:|---:|
| pagina del builder pubblicata | sequenza | 145 | 6,2 | 11,0 | 15,9 | 19,9 | 2790 |
| pagina del builder pubblicata | conc. 8 | 569 | 10,7 | 54,6 | 64,3 | 69,4 | 2790 |
| `/sitemap.xml` | sequenza | 233 | 4,1 | 5,6 | 7,9 | 12,4 | 391 |
| `/sitemap.xml` | conc. 8 | 768 | 9,7 | 18,0 | 23,8 | 35,6 | 391 |
| `/robots.txt` | sequenza | 259 | 3,6 | 5,8 | 9,3 | 10,5 | 26 |
| `/robots.txt` | conc. 8 | 999 | 7,2 | 15,6 | 19,3 | 21,4 | 26 |
| `/css/componenti.css` | sequenza | 241 | 3,8 | 6,6 | 9,1 | 13,4 | 6361 |
| `/css/componenti.css` | conc. 8 | 930 | 7,9 | 15,7 | 19,6 | 19,9 | 6361 |
| `/health/ready` | sequenza | 125 | 7,7 | 10,7 | 13,2 | 16,8 | 66 |
| `/health/ready` | conc. 8 | 544 | 13,9 | 24,5 | 29,5 | 31,8 | 66 |

## Come leggerla

- Le pagine pubbliche rispondono a pochi millisecondi in sequenza perché il contenuto è in cache (`dvcms.cache.accessi`); la prima richiesta dopo una pubblicazione paga il caricamento dal database. Il p95 con concorrenza 8 (≈55 ms) include la contesa di CPU con il client e con SQL Server sulla stessa macchina.
- `/health/ready` è più lenta di una pagina in cache perché esegue davvero i controlli (apertura del database, elenco delle migrazioni, elenco dell'archivio): va usata come sonda con intervalli di secondi, non come endpoint ad alta frequenza.
- **Non misurato:** pagine non in cache, area admin (editor, salvataggio del builder), upload e servizio dei media di grandi dimensioni, concorrenza elevata, rete reale e TLS, database remoto, più istanze (la cache in memoria è per istanza). Da rifare in un ambiente di pre-produzione prima del pilot (M8), con una soglia concordata (es. p95 pagina pubblica sotto un valore deciso con il cliente) e con uno strumento di carico adeguato se serve capacità, non solo regressione.

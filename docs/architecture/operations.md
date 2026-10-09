# Operatività, hosting e resilienza

## Architettura iniziale

- Reverse proxy/CDN/WAF/TLS secondo hosting.
- Una o più istanze stateless ASP.NET Core.
- SQL Server gestito o operato con backup e monitoraggio affidabili (target primario); mantenere PostgreSQL nella matrice di compatibilità supportata.
- Object storage per media e file.
- Cache distribuita quando il deployment ha più istanze o richiede cache condivisa.
- Worker per outbox, email, webhook, trasformazione immagini e sitemap.

Non introdurre Kubernetes, broker o servizi separati per moda. Scegliere l'hosting in base a competenze, costi, residenza dati, SLA e carico.

## Configurazione e segreti

- Configurazione tramite provider .NET (file locali non segreti, environment variables o secret manager in produzione).
- Mai committare password, connection string reali, API key o certificati privati.
- Separare ambienti dev/test/staging/prod.
- Verificare le configurazioni di dominio e proxy per ogni ambiente.

## Background work e outbox

Ogni evento che non può essere perso (publish, ordine, pagamento, webhook) deve avere un meccanismo di retry/idempotenza. Una tabella outbox scritta nella stessa transazione del cambiamento di dominio è il default iniziale. Un worker reclama i messaggi, li processa e marca il risultato; gestire retry con backoff e dead-letter/failed state.

## Cache

- Distinguere output cache pubblica da cache di dati.
- Includere in chiave le dimensioni che determinano il risultato (site, path, revisione, locale e contesto ove necessario).
- Invalidare tramite eventi/outbox al publish, cambio tema, modifica prodotto o asset.
- Non condividere cache di contenuti privati/utente-specifici.

## Osservabilità

- Log strutturati con correlation ID, tenant/site ID dove appropriato e senza payload sensibili.
- Metriche per errori HTTP, latenza, query, cache, coda worker, publish e checkout.
- Health checks per DB, storage e dipendenze essenziali.
- Alert per fallimenti ripetuti, queue lag, esaurimento storage e backup falliti.

## Backup e disaster recovery

Definire prima della produzione RPO/RTO, retention e responsabilità operative. Testare restore di database e oggetti media; un backup mai ripristinato non è una garanzia verificata.

## Ambienti

- Local: SQL Server come default; PostgreSQL disponibile come secondo target. Usare container o installazioni documentate e storage locale solo per sviluppo.
- CI: DB dedicato effimero, migrazioni e test automatici.
- Staging: domini/test provider e dati non reali.
- Production: TLS, secret manager, backup, monitoring, deploy progressivo e rollback.

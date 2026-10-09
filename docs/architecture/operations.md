# Operatività, hosting e resilienza

## Architettura iniziale

- Reverse proxy/CDN/WAF/TLS secondo hosting.
- Una o più istanze stateless ASP.NET Core.
- SQL Server gestito o operato con backup e monitoraggio affidabili.
- Object storage per media e file.
- Cache distribuita quando il deployment ha più istanze o richiede cache condivisa.
- Worker per outbox, email, webhook, trasformazione immagini e sitemap.

Non introdurre Kubernetes, broker o servizi separati per moda. Scegliere l'hosting in base a competenze, costi, residenza dati, SLA e carico.

## Configurazione e segreti

- Configurazione tramite provider .NET (file locali non segreti, environment variables o secret manager in produzione).
- Mai committare password, connection string reali, API key o certificati privati.
- Separare ambienti dev/test/staging/prod.
- Verificare le configurazioni di dominio e proxy per ogni ambiente.

## Domini e certificati (procedura manuale)

Tenant e domini sono gestiti dagli operatori; i certificati dall'hosting tramite reverse proxy (ADR-0014). Il runbook da completare prima del primo cliente copre:

1. Richiesta del dominio da parte del cliente e verifica del possesso fuori banda.
2. Configurazione DNS verso l'infrastruttura.
3. Certificato TLS: emesso e rinnovato dall'hosting tramite reverse proxy; verificare che il nuovo dominio sia coperto.
4. Registrazione del dominio nell'area di piattaforma e marcatura come verificato (chi e quando).
5. Verifica che l'host risolva il sito corretto e che host non registrati ricevano risposta controllata.
6. Monitoraggio della validità dei certificati (a cura dell'hosting, con un controllo periodico).

## Email

Il canale di invio (Microsoft Graph, Gmail, SendGrid o SMTP, ADR-0015) è configurato per tenant dal `PlatformAdmin` nel menu "Platform"; i segreti sono cifrati nel database. La chiave di cifratura sta nella configurazione dell'ambiente (non nel repository) e va salvata separatamente dal database; senza di essa i segreti non sono leggibili (ADR-0015).

## Password del PlatformAdmin

Il `PlatformAdmin` non ha recupero password e per ora non è previsto uno strumento dedicato: se serve un reset lo gestisce l'operatore a mano sul database, con una procedura che definirà in seguito (da descrivere nel runbook, annotando ogni reset).

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

- Local: SQL Server (container o installazione documentata) e storage locale solo per sviluppo.
- CI: DB dedicato effimero, migrazioni e test automatici.
- Staging: domini/test provider e dati non reali.
- Production: TLS, secret manager, backup, monitoring, deploy progressivo e rollback.

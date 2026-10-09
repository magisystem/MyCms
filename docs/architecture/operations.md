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

Tenant, domini e certificati sono gestiti dagli operatori (ADR-0014). Il runbook da completare prima del primo cliente copre:

1. Richiesta del dominio da parte del cliente e verifica del possesso fuori banda.
2. Configurazione DNS verso l'infrastruttura.
3. Emissione e installazione del certificato TLS sul reverse proxy/hosting.
4. Registrazione del dominio nell'area di piattaforma e marcatura come verificato (chi e quando).
5. Verifica che l'host risolva il sito corretto e che host non registrati ricevano risposta controllata.
6. Monitoraggio scadenze dei certificati con promemoria o alert.

## Email

Il canale di invio (Microsoft Graph, Gmail, SendGrid o SMTP, ADR-0015) è configurato per tenant dal `PlatformAdmin` nel menu "Platform"; i segreti sono cifrati nel database e le chiavi di cifratura (Data Protection) sono persistite e incluse nei backup. Per i messaggi senza tenant c'è una configurazione di piattaforma negli appsettings/segreti di ambiente. Prima del primo invio in produzione, a mano: per ogni tenant, configurare l'app/account del canale scelto (registrazione app in Microsoft Entra, progetto OAuth Google, account SendGrid o server SMTP), il dominio mittente con SPF, DKIM e DMARC, e verificare la consegna; monitorare errori e code di invio dal worker.

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

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

## Seeding del tenant primario

Prima del primo avvio in un ambiente si esegue il seeding (ADR-0014): configurazione `Seed:TenantPrimario` nel file di ambiente non versionato, password del `PlatformAdmin` da segreto o variabile d'ambiente, poi `DvCms.Web seed` o avvio con `Seed:EseguiAllAvvio`. Variabili: `Seed__PlatformAdmin__Email`, `Seed__PlatformAdmin__NomeVisualizzato`, `Seed__PlatformAdmin__Password`. Il seeding è idempotente e si può rilanciare; dopo il primo successo si disattiva `Seed:EseguiAllAvvio`. I tenant dei clienti non si seminano: li crea il `PlatformAdmin` dal menu "Platform".

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

### Attività manuali per canale (ADR-0015)

Per ogni canale, oltre ai valori inseriti nella schermata del tenant, servono queste attività fuori dall'applicazione. In tutti i casi il dominio del mittente deve avere SPF, DKIM e DMARC corretti, e la prova di invio dalla schermata del tenant è il controllo finale.

- **SMTP:** un server che accetti le credenziali indicate; scegliere la sicurezza della connessione (`StartTls` o `TlsImmediato`); il certificato del server deve essere valido.
- **Microsoft Graph:** in Microsoft Entra ID registrare un'applicazione (a tenant singolo), creare un segreto client (annotare scadenza e rinnovo), aggiungere il permesso **applicativo** `Mail.Send` della Microsoft Graph API e concedere il consenso dell'amministratore. Limitare l'app alla sola casella mittente con una *Application Access Policy* di Exchange Online (altrimenti `Mail.Send` consente di inviare da qualunque casella del tenant Microsoft). Valori: tenant (nome di dominio o id), id client, segreto client, casella mittente (deve esistere con una licenza o come casella condivisa).
- **Gmail API:** in Google Cloud creare un progetto, abilitare la *Gmail API*, creare un **account di servizio** e una chiave JSON (da incollare nel campo credenziali: è un segreto, cifrato nel database). In Google Workspace, console di amministrazione → Sicurezza → Controlli API → **Delega a livello di dominio**: autorizzare l'ID client dell'account di servizio con lo scope `https://www.googleapis.com/auth/gmail.send`. L'utente mittente è un utente reale del dominio Workspace. Senza delega l'invio fallisce con un errore di autorizzazione. Gli account Gmail personali non supportano la delega: in quel caso usare SMTP.
- **SendGrid:** creare un account, verificare il dominio mittente (*Domain Authentication*, con i record DNS forniti) o almeno un mittente singolo, creare una chiave API con il solo permesso *Mail Send* e usarla nel campo chiave. Ruotare la chiave in caso di sospetta esposizione.

## Password del PlatformAdmin

Il `PlatformAdmin` non ha recupero password e per ora non è previsto uno strumento dedicato: se serve un reset lo gestisce l'operatore a mano sul database, con una procedura che definirà in seguito (da descrivere nel runbook, annotando ogni reset).

## Background work e outbox

Ogni evento che non può essere perso (publish, ordine, pagamento, webhook) deve avere un meccanismo di retry/idempotenza. Una tabella outbox scritta nella stessa transazione del cambiamento di dominio è il default iniziale. Un worker reclama i messaggi, li processa e marca il risultato; gestire retry con backoff e dead-letter/failed state.

## Cache

- Testi statici: cache in memoria per lingua, invalidata alla modifica; l'invalidazione è solo locale all'istanza (decisione per ora); prima di usare più istanze serve un meccanismo condiviso (ad esempio una versione dei testi controllata periodicamente) o una cache distribuita.
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

## Outbox e worker (ADR-0010, C-007)

- Gli eventi (pubblicazione e ritiro di una pagina oggi; email da A-005) si scrivono in `MessaggiOutbox` con la stessa `SaveChanges` della modifica di dominio. Il worker (`ServizioOutbox`, nello stesso processo web in M1b) li elabora a lotti: prenotazione atomica con `UPDLOCK, READPAST` (più istanze non si pestano), gestore per tipo (`IGestoreOutbox`), esito `Completato`.
- Consegna **almeno una volta**: un gestore può ricevere due volte lo stesso messaggio (ad esempio dopo un arresto) e deve tollerarlo. La chiave di idempotenza (`ChiaveIdempotenza`, unica per tenant) evita di accodare due volte lo stesso evento.
- Errori: ritentativo con attesa 10 s, 20 s, 40 s... al massimo un'ora; dopo `Outbox:TentativiMassimi` (predefinito 8) il messaggio diventa `Fallito` e resta in tabella per l'analisi. Un tipo senza gestore fallisce subito. L'ultimo errore è salvato (tipo e testo dell'eccezione, troncato): i gestori non devono mettere segreti nei messaggi di errore.
- Configurazione: `Outbox:Abilitato` (predefinito vero), `Outbox:IntervalloSecondi` (5), `Outbox:DimensioneLotto` (20), `Outbox:TentativiMassimi` (8). Da monitorare: messaggi `Pendente` vecchi e messaggi `Fallito`.
- La versione production-ready (worker separato, metriche, ripulitura dei messaggi completati) è G-001; l'invalidazione della cache delle pagine pubbliche si aggancia a `GestorePaginaPubblicata` con G-002.

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

## Configurazione di sicurezza dietro un reverse proxy (G-005)

- `ProxyAttendibili:Indirizzi` / `ProxyAttendibili:Reti` (CIDR): senza, le intestazioni `X-Forwarded-*` sono ignorate e fuori da Development l'avvio scrive un avviso. Dietro un proxy è obbligatorio: altrimenti tutti i client hanno l'indirizzo del proxy (i limiti per indirizzo diventano globali), lo schema resta `http` (link delle email, HSTS) e l'host non viene letto dal proxy.
- `Sicurezza:ChiaviCookie:Percorso`: cartella persistente e condivisa fra le istanze per le chiavi di sessione e antiforgery (da un volume, con permessi riservati all'utente del processo; non è la chiave dei segreti dei tenant, che è `Sicurezza:ChiaveSegreti`).
- `Sicurezza:Csp:SoloSegnalazione=true`: la CSP diventa `Content-Security-Policy-Report-Only` (utile per provare modifiche senza bloccare nulla).

## Cache dei contenuti pubblici (G-002)

- **Cosa si memorizza:** in memoria, per sito, il risultato della risoluzione di una pagina (il contenuto o il reindirizzamento 301), l'elenco delle lingue, il tema in vigore e la sitemap. **Mai** le anteprime né nulla dell'area `/admin`, le assenze (404: chi cerca indirizzi a caso non riempie la memoria), i media, né dati personali (carrello, account, checkout non esistono ancora e non andranno mai qui). La chiave è sito + lingua + slug: tenant e sito derivano dall'host, quindi nessuna voce è condivisa fra tenant (test di isolamento).
- **Invalidazione transazionale:** ogni modifica che cambia ciò che vede il pubblico (salvataggio, pubblicazione e ritiro di una pagina, cambio di slug, eliminazione di un reindirizzamento, tema, lingue e nome del sito) scrive una riga in `InvalidazioniCache` **nella stessa `SaveChanges`** della modifica. Subito dopo il salvataggio l'istanza che ha scritto svuota la cache del sito (generazione del sito incrementata: le voci vecchie diventano irraggiungibili e vengono sfoltite quando serve spazio). Ogni istanza legge il registro ogni `Cache:IntervalloLetturaSecondi` (2) e invalida i siti indicati: una modifica fatta su una macchina arriva alle altre in pochi secondi, senza broker. Il registro si ripulisce da solo (due ore, manutenzione).
- **Rete di sicurezza:** ogni voce vale al massimo `Cache:SecondiValidita` (300 s); è il limite di obsolescenza per modifiche che non passano dall'applicazione (SQL a mano, ripristini di backup). Per forzare, si inserisce una riga in `InvalidazioniCache` per il sito o si riavvia l'applicazione.
- **Limiti e spegnimento:** `Cache:VociMassime` (5000: sfoltisce prima le voci scadute o di generazioni superate, poi le più vecchie), `Cache:Abilitata=false` per disattivarla del tutto (ogni richiesta legge dal database). Le risposte HTTP restano `Cache-Control: public, max-age=300` solo per sitemap e robots, `max-age=3600` per i media; le pagine HTML non dichiarano ancora una cache pubblica (CDN e cache del browser arriveranno con un piano di purge).
- **Evento di pubblicazione:** gli eventi `PaginaPubblicata`, `PaginaRitirata` e `PaginaRinominata` dell'outbox restano per altri consumatori (webhook, indicizzazione); la cache non dipende dal worker, quindi una coda ferma non lascia contenuti vecchi.

## Archivio dei file (D-003)

- Provider scelto con `Archivio:Provider`; oggi esiste solo `FileSystem` (un provider sconosciuto ferma l'avvio). `Archivio:Percorso` è la cartella radice; se vuota si usa `archivio` accanto all'applicazione, che in produzione **non va lasciata lì**: va su un volume persistente fuori dalla cartella del deploy. La cartella contiene i file di tutti i tenant sotto `{tenant}/{sito}/{risorsa}` e deve essere leggibile e scrivibile solo dall'utente del processo.
- **Backup:** il database da solo non basta: metadati e file vivono in posti diversi, quindi il backup della cartella dell'archivio va fatto con la stessa cadenza del database e ripristinato insieme (un file senza riga è un orfano innocuo; una riga senza file risponde 404). I provider a oggetti (S3, Azure Blob) arriveranno come nuovi adapter di `IArchivioOggetti` senza toccare il resto.
- Limiti di caricamento in `Media:*` (vedi `security.md`). Dietro un reverse proxy il limite di dimensione del corpo del proxy deve essere almeno quello del file più grande ammesso.

## Password del PlatformAdmin

Il `PlatformAdmin` non ha recupero password e per ora non è previsto uno strumento dedicato: se serve un reset lo gestisce l'operatore a mano sul database, con una procedura che definirà in seguito (da descrivere nel runbook, annotando ogni reset).

## Autenticazione a due fattori (G-006)

- `Sicurezza:DueFattori:Obbligo`: `Amministratori` (predefinito), `PlatformAdmin` o `Nessuno`. Con l'obbligo attivo serve anche `Sicurezza:ChiaveSegreti` (cifra i segreti TOTP). `Sicurezza:DueFattori:Emittente` (predefinito `DvCms`) è il nome mostrato nell'app di autenticazione.
- Primo avvio con l'obbligo predefinito: il `PlatformAdmin` creato dal popolamento accede con la password e viene portato a `/admin/sicurezza` per configurare l'app; lo stesso vale per ogni `TenantAdmin` al primo accesso.
- Dispositivo perso: `DvCms.Web reset-2fa <email>` (solo `PlatformAdmin`, codice di uscita 0/1) oppure, per i `TenantAdmin`, "Reimposta 2FA" nel dettaglio del tenant. Procedura completa e verifica dell'identità in [`docs/operations/runbook-due-fattori.md`](../operations/runbook-due-fattori.md).
- Il backup del database da solo non basta: i segreti TOTP sono leggibili solo con la stessa chiave dei segreti ([runbook di backup](../operations/backup-e-ripristino.md)).

## Background work e outbox

Ogni evento che non può essere perso (publish, ordine, pagamento, webhook) deve avere un meccanismo di retry/idempotenza. Una tabella outbox scritta nella stessa transazione del cambiamento di dominio è il default iniziale. Un worker reclama i messaggi, li processa e marca il risultato; gestire retry con backoff e dead-letter/failed state.

## Cache

- Testi statici: cache in memoria per lingua, invalidata alla modifica; l'invalidazione è solo locale all'istanza (decisione per ora); prima di usare più istanze serve un meccanismo condiviso (ad esempio una versione dei testi controllata periodicamente) o una cache distribuita.
- Distinguere output cache pubblica da cache di dati.
- Includere in chiave le dimensioni che determinano il risultato (site, path, revisione, locale e contesto ove necessario).
- Invalidare al publish, cambio tema, modifica prodotto o asset: per i contenuti pubblici del sito è implementato con un registro transazionale letto da ogni istanza (sezione "Cache dei contenuti pubblici"); prodotti e asset useranno lo stesso registro quando ci saranno.
- Non condividere cache di contenuti privati/utente-specifici.

## Osservabilità

- Log strutturati con correlation ID, tenant/site ID dove appropriato e senza payload sensibili.
- Metriche per errori HTTP, latenza, query, cache, coda worker, publish e checkout.
- Health checks per DB, storage e dipendenze essenziali: attuati (`/health/live`, `/health/ready`), vedi [`docs/operations/osservabilita.md`](../operations/osservabilita.md), che elenca anche metriche e alert; baseline in [`docs/operations/prestazioni.md`](../operations/prestazioni.md).
- Alert per fallimenti ripetuti, queue lag, esaurimento storage e backup falliti.

## Backup e disaster recovery

Il runbook è in [`../operations/backup-e-ripristino.md`](../operations/backup-e-ripristino.md): cosa si salva (database, archivio dei file, **chiave dei segreti separata**), obiettivi proposti (RPO/RTO/retention, da confermare), procedura di ripristino con i controlli (`verifica-archivio`, `migra`, coda), limiti e registro delle prove. Un backup mai ripristinato non è una garanzia verificata: la prova di ripristino è automatica in CI (`RipristinoTest`) e va ripetuta su infrastruttura reale ogni trimestre.

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
- **Stato consultabile (G-001):** `/admin/platform/coda` (solo `PlatformAdmin`) mostra i contatori (falliti, in attesa con il più vecchio, completati) e l'elenco dei messaggi per stato con tipo, tenant, tentativi e **ultimo errore**; il contenuto dei messaggi non viene mai mostrato (può essere riservato). Un messaggio fallito si può **riprovare** (torna in coda con i tentativi azzerati; l'aggiornamento è condizionato allo stato, quindi un doppio clic non fa danni) o **scartare**; entrambe le azioni sono nell'audit (`RitentativoMessaggio`, `ScartoMessaggio`). Un attore che non è `PlatformAdmin` riceve 403 e le chiamate dirette al servizio sono negate e registrate.
- **Metriche:** meter `DvCms.Outbox` (OpenTelemetry o `dotnet-counters`): `dvcms.outbox.elaborati` con i tag `tipo` ed `esito` (`riuscito`, `ritentato`, `fallito`) e `dvcms.manutenzione.righe_eliminate` con il tag `tabella`. Allarmi consigliati: `fallito` > 0, e messaggi `Pendente` con il più vecchio oltre pochi minuti (il riepilogo della schermata lo mostra).
- **Manutenzione dei dati scaduti:** `ServizioManutenzione` (ogni `Manutenzione:IntervalloMinuti`, 60; il primo passaggio 2 minuti dopo l'avvio; `Manutenzione:Abilitata`) elimina a lotti (`Manutenzione:DimensioneLotto`, 1000) i messaggi `Completato` oltre `Manutenzione:GiorniConservazioneCompletati` (7) e i `Fallito` oltre `Manutenzione:GiorniConservazioneFalliti` (90), le richieste di recupero password e gli inviti conclusi, annullati o scaduti oltre `Manutenzione:GiorniConservazioneAccessi` (30). **Un messaggio `Pendente` non si elimina mai**, per vecchio che sia. Le istruzioni sono idempotenti: più istanze insieme non si ostacolano.
- **Più istanze e worker dedicato:** la prenotazione con `UPDLOCK, READPAST` rende sicuro avere il worker attivo su più istanze. Per un'istanza che serve solo le richieste web si imposta `Outbox:Abilitato=false` e `Manutenzione:Abilitata=false`; per un'istanza dedicata al lavoro in background basta avviare la stessa applicazione con quei valori attivi su un'altra macchina (nessun processo separato da mantenere finché il carico non lo giustifica).

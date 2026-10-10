# Backup e ripristino (G-004)

Questo runbook dice **cosa** si salva, **come** si ripristina e **come si verifica** che il ripristino sia buono. Le istruzioni SQL sono le stesse eseguite dal test automatico `RipristinoTest` (progetto `DvCms.IntegrationTests`, cartella `Ripristino`): se si cambia una procedura qui si cambia anche il test, e il test gira a ogni modifica in CI su un SQL Server vero.

Gli obiettivi (RPO/RTO, retention) sono **proposte da confermare** (vedi `docs/open-questions.md`): il software li rende possibili, la scelta è del committente.

## Cosa serve per ripartire

| Cosa | Dove sta | Come si salva | Se si perde |
|---|---|---|---|
| Database (tenant, pagine, revisioni, account, audit, coda, impostazioni, metadati media) | SQL Server | Backup completo + backup del log (vedi sotto) | Si perde tutto: è la fonte di verità |
| Archivio dei file (immagini caricate) | Cartella `Archivio:Percorso` (oggi file system) | Copia della cartella, con la stessa cadenza del database | I file mancanti rispondono 404; le righe restano (si vedono con `verifica-archivio`) |
| **Chiave dei segreti** `Sicurezza:ChiaveSegreti` | Configurazione di ambiente (secret manager, variabile d'ambiente) | **Copia separata** dal backup del database, in un luogo diverso | I segreti dei tenant (password SMTP, chiavi API, credenziali Gmail/Graph) **non si leggono più**: vanno reinseriti uno per uno dalla schermata del tenant. Il database senza la chiave non li rivela |
| Altra configurazione (`appsettings.*.json`, variabili d'ambiente, certificati TLS) | Repository (solo i valori non segreti) e secret manager | Come per il resto dell'infrastruttura | L'applicazione non parte o parte con valori sbagliati |

La chiave dei segreti **non** sta mai nello stesso backup del database: se coincidessero, chi ruba il backup leggerebbe i segreti. Va conservata cifrata, con almeno due copie in posti diversi e accessibile a più di una persona.

## Obiettivi proposti (da confermare)

| Obiettivo | Proposta | Come si ottiene |
|---|---|---|
| RPO (dati persi al massimo) | 15 minuti per il database, 24 ore per i file caricati | Modello di recupero `FULL` con backup del log ogni 15 minuti; copia dell'archivio ogni notte (o replica continua se il provider lo offre) |
| RTO (tempo per ripartire) | 4 ore | Procedura sotto, provata; ambiente di ripristino sempre pronto o ricreabile da zero |
| Retention | Completi: 35 giorni; log: 7 giorni; almeno un backup completo mensile per 12 mesi | Politica del servizio di backup |
| Prova di ripristino | Ogni trimestre e dopo ogni cambio di infrastruttura o di versione maggiore di SQL Server | Procedura sotto + `verifica-archivio` |

## Backup

Il database usa il modello di recupero `FULL` in produzione (`ALTER DATABASE [DvCms] SET RECOVERY FULL`), altrimenti i backup del log non sono possibili.

```sql
-- Completo (ogni notte), con somma di controllo: un blocco danneggiato fa fallire il backup invece di finire in silenzio nel file.
BACKUP DATABASE [DvCms] TO DISK = N'/percorso/DvCms-completo-AAAAMMGG.bak' WITH INIT, CHECKSUM, COMPRESSION;

-- Del log (ogni 15 minuti)
BACKUP LOG [DvCms] TO DISK = N'/percorso/DvCms-log-AAAAMMGG-HHMM.trn' WITH CHECKSUM, COMPRESSION;

-- Controllo subito dopo ogni backup (non sostituisce la prova di ripristino, ma scopre i file illeggibili)
RESTORE VERIFYONLY FROM DISK = N'/percorso/DvCms-completo-AAAAMMGG.bak' WITH CHECKSUM;
```

- I file di backup vanno **fuori dal server del database** (altro disco, altra macchina, storage a oggetti con cancellazione ritardata o immutabilità) e **cifrati** (`WITH ENCRYPTION` o cifratura del servizio di backup): contengono hash delle password, indirizzi email e i segreti cifrati.
- Archivio dei file: copia della cartella `Archivio:Percorso`, per esempio `rsync -a --delete-after` verso un volume di backup, **dopo** il backup del database o con un'istantanea del volume. L'ordine conta poco perché i file sono immutabili (non si sovrascrivono mai): un file senza riga è un orfano innocuo; una riga senza file si vede con `verifica-archivio` e risponde 404.
- Un backup fallito è un allarme (vedi "Osservabilità" in `operations.md`): nessun `BACKUP` riuscito nelle ultime 24 ore = intervento.

## Ripristino

Prima di cominciare: **fermare** l'applicazione (o impostare `Outbox:Abilitato=false` e `Manutenzione:Abilitata=false`, vedi il punto sulla coda), così nessun processo scrive mentre si ripristina.

1. **Database.** Ripristino completo, poi i log fino al punto voluto. Se il database esiste ancora e va sostituito, `ALTER DATABASE [DvCms] SET SINGLE_USER WITH ROLLBACK IMMEDIATE` prima, `MULTI_USER` dopo.
   ```sql
   RESTORE FILELISTONLY FROM DISK = N'/percorso/DvCms-completo-AAAAMMGG.bak';   -- nomi logici dei file

   RESTORE DATABASE [DvCms] FROM DISK = N'/percorso/DvCms-completo-AAAAMMGG.bak'
       WITH CHECKSUM, NORECOVERY,
            MOVE N'<nome-logico-dati>' TO N'/var/opt/mssql/data/DvCms.mdf',
            MOVE N'<nome-logico-log>'  TO N'/var/opt/mssql/data/DvCms_log.ldf';

   RESTORE LOG [DvCms] FROM DISK = N'/percorso/DvCms-log-AAAAMMGG-HHMM.trn' WITH CHECKSUM, NORECOVERY;   -- ripetere in ordine
   -- Per fermarsi a un istante preciso: ... WITH STOPAT = '2026-10-10T10:45:00', NORECOVERY

   RESTORE DATABASE [DvCms] WITH RECOVERY;   -- rende il database utilizzabile
   ```
   Per ripristinare con un altro nome (prova, o confronto prima di sostituire) basta cambiare il nome del database e i percorsi di `MOVE`: è ciò che fa il test.
2. **Archivio dei file.** Rimettere la cartella nel percorso di `Archivio:Percorso` (stesso utente e stessi permessi: lettura e scrittura solo per il processo dell'applicazione).
3. **Chiave dei segreti.** Impostare `Sicurezza:ChiaveSegreti` con **la stessa chiave** in uso quando i dati sono stati scritti. Con una chiave diversa l'applicazione parte ma i segreti non si decifrano (invii email dei tenant con credenziali fallirebbero; il database non rivela nulla).
4. **Schema.** Il backup contiene lo schema della versione in cui è stato fatto. Distribuire la **stessa versione** dell'applicazione, oppure una successiva dopo `DvCms.Web migra` (le migrazioni non partono mai da sole all'avvio). Una versione *più vecchia* del database non si usa con codice più nuovo senza migrare, e viceversa.
5. **Controlli** (tutti, prima di riaprire al pubblico):
   - `dotnet DvCms.Web.dll verifica-archivio`: codice di uscita 0 e nessun "oggetto mancante" né "impronta diversa". Gli "oggetti senza riga" sono avvisi (file di caricamenti dopo il punto di ripristino, o di risorse eliminate): si possono lasciare o cancellare a mano.
   - `dotnet DvCms.Web.dll migra` non deve applicare nulla (se applica, il backup era di una versione più vecchia: va bene, ma annotarlo).
   - Accesso all'admin con un account noto; apertura di una pagina pubblica e di un'immagine; schermata "Coda" (`/admin/platform/coda`); prova di invio email dalla schermata del tenant (verifica la chiave dei segreti).
6. **Coda dei messaggi.** I messaggi `Pendente` nel database ripristinato sono quelli del momento del backup: alcuni potrebbero essere già stati elaborati *dopo* quel momento (email inviate, per esempio) e, riavviando il worker, verrebbero **elaborati di nuovo** (consegna "almeno una volta"). Prima di riattivare il worker, controllare la schermata "Coda": scartare i messaggi `Pendente` che non vanno ripetuti (email di recupero password e inviti scaduti nel frattempo, in particolare) oppure accettare i duplicati. Poi togliere `Outbox:Abilitato=false`.
7. **Cache.** La cache è in memoria: il riavvio dell'applicazione la svuota. Se l'applicazione è rimasta accesa durante un ripristino (da evitare), basta riavviarla.
8. **Sessioni.** Gli account restano com'erano al momento del backup; le sessioni aperte restano valide finché valgono i loro cookie (vedi `security.md` per le chiavi dei cookie). Dopo un incidente di sicurezza (non un guasto) si cambiano le password o il timbro di sicurezza degli account, e si ruotano le chiavi dei segreti.

## Ripristino parziale (un tenant)

Non esiste, e non va improvvisato con `INSERT … SELECT` fra database: le tabelle sono collegate da chiavi composte e dai puntatori fra pagine e revisioni. Per recuperare dati di un solo tenant si ripristina il backup in un database **con un altro nome**, si leggono lì i dati e li si reinseriscono con le funzioni dell'applicazione (per esempio ripristinando una revisione di una pagina dall'admin, dopo aver letto il contenuto dal database di prova).

## Verifica eseguita

Prova su un ambiente di test (SQL Server 2022 su Linux in container, database di prova con due tenant, pagine pubblicate, un'immagine caricata, impostazioni email con segreto cifrato, un utente per tenant e messaggi nella coda):

| Controllo | Esito |
|---|---|
| `BACKUP DATABASE … WITH CHECKSUM, COMPRESSION` e `RESTORE VERIFYONLY … WITH CHECKSUM` | Riusciti |
| Ripristino con altro nome e file spostati (`MOVE`), `WITH CHECKSUM` | Riuscito |
| Confronto di tutte le tabelle del database (numero di righe e `CHECKSUM_AGG(BINARY_CHECKSUM(*))`, `rowversion` compresi) fra originale e ripristino | Identiche (più di 20 tabelle) |
| Migrazioni da applicare sul ripristino | Nessuna |
| Password di un account verificata con l'hash ripristinato | Corretta |
| Segreto delle impostazioni email decifrato con la chiave originale | Corretto |
| Stesso database con una chiave diversa | I segreti **non** si decifrano e l'errore non contiene né il segreto né la chiave |
| Pagina pubblica risolta dal ripristino; immagine servita con la stessa impronta SHA-256 | Corretto |
| `verifica-archivio` con archivio copiato | Coerente, nessun orfano |
| `verifica-archivio` con file alterato / file cancellato / oggetto in più | Segnalati rispettivamente come "impronta diversa", "mancante" e "orfano" |
| Messaggi della coda nel ripristino | Presenti nello stato del backup |
| Durata di backup, controllo e ripristino del database di prova | Pochi secondi (tutto il test: circa 11 secondi); i tempi su dati reali vanno misurati nella prima prova trimestrale |

Il test gira in CI a ogni modifica (`RipristinoTest`). **Non** copre: il ripristino del log fino a un istante (`STOPAT`) né la copia di backup fuori macchina e la cifratura dei file di backup, che dipendono dall'infrastruttura scelta; vanno provati nella prima prova sull'ambiente di produzione o di staging e registrati qui (data, durata, esito, chi l'ha eseguita).

## Registro delle prove

| Data | Ambiente | Eseguita da | Tipo | Durata | Esito | Note |
|---|---|---|---|---|---|---|
| 2026-10-10 | Test automatico (container SQL Server 2022) | CI e sviluppo | Completo + ripristino con altro nome + archivio + chiave | ~11 s per l'intero test | Riuscita | Procedura e controlli sopra |
| _da fare_ | Staging | _da assegnare_ | Completo + log con `STOPAT`, copia fuori macchina cifrata | | | Prima prova su infrastruttura reale |

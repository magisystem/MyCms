# Strategia provider database: SQL Server primario, PostgreSQL supportato

## Decisione

Il CMS supporta due provider relazionali tramite EF Core:

1. **Microsoft SQL Server — provider primario e predefinito.** Usato negli esempi, nello sviluppo standard, nella suite CI obbligatoria e nel percorso di deployment di riferimento.
2. **PostgreSQL — provider secondario supportato.** Deve continuare a compilare, applicare le proprie migrazioni e superare una suite di compatibilità automatizzata.

Il supporto doppio è un requisito esplicito, non una futura idea. SQL Server ha priorità quando si definiscono esempi e procedure, ma non è accettabile una modifica che rompe PostgreSQL senza una deroga documentata.

## Configurazione e registrazione

- Selezionare il provider tramite una chiave di configurazione esplicita, per esempio `Database:Provider=SqlServer` oppure `Database:Provider=PostgreSql`.
- Il valore predefinito è `SqlServer`; valori non riconosciuti devono causare un errore all'avvio, non un fallback silenzioso.
- Tenere connection string distinte (`ConnectionStrings:SqlServer` e `ConnectionStrings:PostgreSql`) e segreti fuori dal repository.
- Un database esistente appartiene a un provider specifico. Non tentare di passare da SQL Server a PostgreSQL puntando a un'altra connection string: servono migrazione dei dati e validazione separata.
- L'applicazione non deve contenere logica di business che dipenda dal provider. Le differenze devono restare in configurazioni Infrastructure, migrazioni e adapter isolati.

La registrazione concreta del provider e il wiring di design-time EF Core vanno implementati e documentati durante il bootstrap. Il codice deve usare i package provider ufficiali compatibili con la major di EF Core selezionata.

## Migrazioni EF Core

Mantenere **due serie indipendenti di migrazioni** e i rispettivi snapshot del modello:

- `Migrations/SqlServer/` o assembly di migrazione SQL Server;
- `Migrations/PostgreSql/` o assembly di migrazione PostgreSQL.

È accettabile utilizzare lo stesso `DbContext` e assembly separati, o contesti di design-time distinti se semplificano la configurazione. La scelta concreta deve essere registrata in codice e in un comando documentato. Non mischiare file migration generati da provider diversi nella stessa serie.

Ogni PR che modifica lo schema deve:

1. modificare il modello EF;
2. generare la migrazione SQL Server;
3. generare la migrazione PostgreSQL;
4. revisionare il codice generato, senza affidarsi alla generazione automatica come prova di correttezza;
5. applicare tutte e due le serie a database vuoti in CI;
6. verificare l'upgrade da almeno una versione precedente quando il progetto avrà schema già distribuiti.

Le migration possono avere dettagli SQL specifici del provider, ma devono rispettare lo stesso contratto semantico di dominio. Se una capability non è implementabile in modo ragionevole su entrambi, proporre un ADR prima di aggiungerla al modello comune.

Riferimento: [EF Core — Migrations with Multiple Providers](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers).

## Compatibilità del modello dati

Per mantenere entrambi i target:

- preferire `Guid` generati dall'applicazione per gli identificatori condivisi, se non emerge un requisito diverso; configurare e testare esplicitamente chiavi e indici;
- definire esplicitamente precisione e scala per importi monetari con `decimal` e memorizzare anche la valuta;
- usare UTC per gli istanti, documentando il tipo CLR e la mappatura per ogni provider;
- normalizzare gli slug in forma canonica lato applicazione e imporre vincoli univoci composti tenant/sito; non basare la correttezza degli slug sulle differenze di collation;
- preferire mapping EF Core portabili e vincoli relazionali standard;
- non usare direttamente tipi nativi come array PostgreSQL o funzioni SQL Server dentro Domain/Application;
- evitare query raw SQL specifiche del provider nel codice condiviso. Se inevitabili, isolare ogni implementazione e coprirla con test specifici.

### Documenti JSON

I documenti CMS sono dati strutturati, non query applicative arbitrarie. Serializzare/validare il documento a livello applicativo e non richiedere query dentro JSON per la prima versione.

Le colonne e le migration possono usare tipi differenti per provider (ad esempio `nvarchar(max)` per SQL Server e `jsonb` per PostgreSQL) se ciò è utile, ma i mapping devono essere configurati esplicitamente e verificati su entrambi i provider. Non introdurre indici o query JSON specifiche di un motore senza una ragione misurabile e un piano di compatibilità.

### Test e differenze di comportamento

SQL Server è il database obbligatorio per tutte le suite d'integrazione principali. La suite PostgreSQL deve almeno verificare:

- bootstrap e tutte le migrazioni da database vuoto;
- vincoli, indici univoci e relazioni tenant/sito;
- query EF Core importanti, ordinamento, paginazione e confronto degli slug;
- transazioni, concorrenza ottimistica e outbox;
- persistenza e recupero dei documenti JSON;
- test d'isolamento cross-tenant;
- flussi critici CMS e Commerce aggiunti successivamente.

Non utilizzare EF Core InMemory o SQLite come sostituto dei test su entrambi i provider. Possono essere utilizzati per test isolati non relazionali solo quando il loro scopo è esplicito.

## Impatto sulla CI e sui costi

La CI deve avere una matrice provider: SQL Server obbligatorio e PostgreSQL obbligatorio prima del merge per modifiche a persistence/schema o a query. Per modifiche puramente UI la suite PostgreSQL può essere condizionata se non tocca persistenza, ma la pipeline completa deve girare regolarmente.

Il supporto a due provider aumenta il costo di test e manutenzione, soprattutto per migrazioni. Non è un'astrazione gratuita: il progetto deve mantenere la parità del modello e delle operazioni critiche. Evitare feature specifiche non necessarie e documentare le eccezioni.

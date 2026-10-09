# Setup di sviluppo locale

Questo file descrive il target di setup; i comandi precisi vanno completati durante il bootstrap, una volta fissati nomi progetto e tool test.

## Prerequisiti

- .NET 10 SDK aggiornato.
- SQL Server (Developer o Express, anche in container) in una versione supportata dal provider EF Core 10.
- Git e un IDE/editor C#.
- Node/npm soltanto per sviluppare e creare il bundle del page builder; non serve per eseguire il backend in produzione.
- Tool di container opzionale per servizi locali.

## Configurazione

- Copiare un file di esempio non segreto in configurazione locale, oppure usare user-secrets/environment variables.
- Non committare password, connection string reali, API key, chiavi di cifratura, certificati o dati cliente. I file di configurazione con segreti (es. `appsettings.Development.json` locale, `appsettings.Production.json`) restano fuori dal repository.
- Il tenant è deciso dall'host: per provare più tenant in locale servono host distinti registrati (vedi "Tenant di test in locale").
- In sviluppo usare domini locali documentati oppure un middleware/test host esplicito; non introdurre bypass non sicuri nella configurazione produzione.
- Lo storage file system è esclusivamente un adapter per lo sviluppo.

## Flusso previsto dopo il bootstrap

1. Ripristinare i pacchetti NuGet.
2. Avviare SQL Server.
3. Configurare la connection string tramite user-secrets o environment variables.
4. Applicare le migrazioni EF Core con il comando documentato dalla solution.
5. Eseguire il seeding del tenant primario e del primo `PlatformAdmin` (ADR-0014): configurazione `Seed:TenantPrimario` e password del `PlatformAdmin` da user-secrets o environment variable, nessuna credenziale predefinita. In `Development` il seeding può creare anche i tenant di test `cms1`/`cms2` da `Seed:TenantSviluppo`.
6. Avviare `DvCms.Web`.
7. Eseguire test unitari e d'integrazione.
8. Per il builder, entrare in `frontend/editor-pagine`, installare le dipendenze dal lockfile e avviare/buildare l'asset secondo lo script documentato.

Gli agenti non devono inventare comandi finali prima che i file di progetto siano stati creati. Aggiornare questa pagina nello stesso PR del bootstrap.


Per i dettagli su migrazioni e test, consultare [Database SQL Server](../architecture/database.md).

## Tenant di test in locale

Il tenant è deciso solo dall'host (ADR-0011), quindi in sviluppo si usano host distinti, tutti risolti sul computer locale. Si usano sottodomini di terzo livello del dominio del committente, ad esempio:

- `cms1.davidemartelli.dev` e `cms2.davidemartelli.dev` come tenant di test;
- `cms.davidemartelli.dev` per il tenant di gestione, più `localhost` (qualunque porta) come alias valido solo in sviluppo.

Questi host puntano al computer locale (record DNS verso `127.0.0.1` o voci nel file hosts) e non sono raggiungibili da internet. Vanno registrati come domini dei tenant di test (verificati a mano, ADR-0014) e non sono mai usati in produzione. Il dominio registrato dei tenant di test è un dato di configurazione/seed locale, non va scritto nel codice. In sviluppo si usa **solo HTTP**: servono il binding di Kestrel su questi nomi e porte HTTP, e i cookie di autenticazione non possono essere `Secure` in `Development`. Il tenant di gestione risponde anche su `localhost` (qualunque porta), solo in `Development` (ADR-0014). Non vanno introdotti bypass non sicuri nella configurazione di produzione: là i certificati sono gestiti dall'hosting tramite reverse proxy, e non sono compito dell'applicazione.

## Asset dell'admin

Il tema admin (Tabler e Tabler Icons) è in `admin-theme/` (ADR-0013). Al bootstrap le cartelle `tabler` e `tabler-icons` si copiano per intero in `src/DvCms.Web/wwwroot/lib/`; le pagine HTML demo non devono essere servite. Gli altri pacchetti di `admin-theme/` non vanno in `wwwroot`. Npm non è necessario per Tabler: `dist/` è già compilato.

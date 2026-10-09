# Setup di sviluppo locale

Questo file descrive il setup di sviluppo. I comandi indicati come "disponibili" sono già presenti nella solution; il resto descrive il target e si completa man mano che le voci del backlog vengono implementate.

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

## Comandi disponibili (A-001)

Dalla radice del repository, con il .NET 10 SDK installato (`global.json` fissa la versione minima; su Ubuntu è disponibile il pacchetto `dotnet-sdk-10.0`):

```bash
dotnet restore DvCms.sln
dotnet build DvCms.sln --configuration Release --no-restore
dotnet test --solution DvCms.sln --configuration Release --no-build
dotnet format DvCms.sln --verify-no-changes   # controllo di formattazione e stile, usato anche dalla CI
```

I test usano xUnit v3 con Microsoft Testing Platform (impostato in `global.json`, richiesto da .NET 10): `dotnet test` richiede l'opzione `--solution`. Le versioni dei pacchetti NuGet sono centralizzate in `Directory.Packages.props`. La CI (`.github/workflows/ci.yml`) esegue gli stessi passaggi.

## SQL Server in locale (A-002)

Un'istanza SQL Server qualsiasi va bene (Developer, Express o container). Esempio con Docker, solo per sviluppo (la password è un esempio locale):

```bash
docker run -d --name sqldvcms -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=<password-locale-robusta>' \
  -p 14330:1433 mcr.microsoft.com/mssql/server:2022-latest
```

**Applicazione.** La stringa di connessione `ConnectionStrings:Predefinita` non sta mai nel repository (`appsettings.json` la lascia vuota e l'applicazione non parte se manca). In sviluppo:

```bash
dotnet user-secrets set "ConnectionStrings:Predefinita" "Server=localhost,14330;Database=DvCms;User Id=sa;Password=<password-locale>;TrustServerCertificate=True" --project src/DvCms.Web
# oppure la variabile d'ambiente ConnectionStrings__Predefinita
```

**Migrazioni.** Si applicano solo con il comando esplicito, mai in automatico all'avvio:

```bash
dotnet run --project src/DvCms.Web -- migra
```

La creazione di nuove migrazioni è descritta in [`convenzioni-codice.md`](convenzioni-codice.md).

**Test di integrazione.** Servono un server SQL Server con permesso di creare ed eliminare database; ogni test usa un database temporaneo. Si indica con la variabile d'ambiente `DVCMS_CONNESSIONE_TEST`:

```bash
export DVCMS_CONNESSIONE_TEST='Server=localhost,14330;User Id=sa;Password=<password-locale>;TrustServerCertificate=True;Encrypt=False'
dotnet test --solution DvCms.sln --configuration Release
```

Senza la variabile i test sul database risultano saltati (non passati); con `CI=true` falliscono. La CI di GitHub avvia un SQL Server effimero come service container.

## Flusso previsto dopo il bootstrap

1. Ripristinare i pacchetti NuGet.
2. Avviare SQL Server.
3. Configurare la connection string tramite user-secrets o environment variables.
4. Applicare le migrazioni EF Core con `dotnet run --project src/DvCms.Web -- migra`.
5. Eseguire il popolamento (seeding) del tenant primario e del primo `PlatformAdmin` (ADR-0014) con `dotnet run --project src/DvCms.Web -- seed`. I dati non segreti stanno in `appsettings.json` (`Seed:TenantPrimario`: nome, sito, domini); email e nome del `PlatformAdmin` e la sua password non sono versionati e vanno impostati con user-secrets o variabili d'ambiente, senza credenziali predefinite:

   ```bash
   dotnet user-secrets --project src/DvCms.Web set "Seed:PlatformAdmin:Email" "operatore@esempio.it"
   dotnet user-secrets --project src/DvCms.Web set "Seed:PlatformAdmin:NomeVisualizzato" "Operatore"
   dotnet user-secrets --project src/DvCms.Web set "Seed:PlatformAdmin:Password" "<password-di-almeno-12-caratteri>"
   ```

   In produzione si usano le variabili `Seed__PlatformAdmin__Email`, `Seed__PlatformAdmin__NomeVisualizzato` e `Seed__PlatformAdmin__Password`. Se la password non è configurata e il terminale è interattivo, il comando la chiede senza mostrarla; negli altri casi, se il `PlatformAdmin` deve essere creato, fallisce. Il comando è idempotente: crea solo ciò che manca, non reimposta password né modifica dati esistenti e segnala nel log le divergenze. Si può anche eseguire all'avvio con `Seed:EseguiAllAvvio=true` (default falso). In `Development` crea inoltre l'alias `localhost` (solo sviluppo) e i tenant di prova `cms1`/`cms2.davidemartelli.dev` da `Seed:TenantSviluppo` (in `appsettings.Development.json`): la password di ciascun `TenantAdmin` è un segreto (`Seed:TenantSviluppo:0:TenantAdmin:Password`, `Seed:TenantSviluppo:1:TenantAdmin:Password`). Fuori da `Development` queste parti sono ignorate.
6. Avviare `DvCms.Web`.
7. Eseguire test unitari e d'integrazione.
8. Per il builder, entrare in `frontend/page-builder`, installare le dipendenze dal lockfile e avviare/buildare l'asset secondo lo script documentato.

Gli agenti non devono inventare comandi non ancora implementati (builder). Aggiornare questa pagina nello stesso PR che li introduce.

Per i dettagli su migrazioni e test, consultare [Database SQL Server](../architecture/database.md).

## Tenant di test in locale

Il tenant è deciso solo dall'host (ADR-0011), quindi in sviluppo si usano host distinti, tutti risolti sul computer locale. Si usano sottodomini di terzo livello del dominio del committente, ad esempio:

- `cms1.davidemartelli.dev` e `cms2.davidemartelli.dev` come tenant di test;
- `cms.davidemartelli.dev` per il tenant di gestione, più `localhost` (qualunque porta) come alias valido solo in sviluppo.

Questi host puntano al computer locale (record DNS verso `127.0.0.1` o voci nel file hosts) e non sono raggiungibili da internet. Vanno registrati come domini dei tenant di test (verificati a mano, ADR-0014) e non sono mai usati in produzione. Il dominio registrato dei tenant di test è un dato di configurazione/seed locale, non va scritto nel codice. In sviluppo si usa **solo HTTP**: servono il binding di Kestrel su questi nomi e porte HTTP, e i cookie di autenticazione non possono essere `Secure` in `Development`. Il tenant di gestione risponde anche su `localhost` (qualunque porta), solo in `Development` (ADR-0014). Non vanno introdotti bypass non sicuri nella configurazione di produzione: là i certificati sono gestiti dall'hosting tramite reverse proxy, e non sono compito dell'applicazione.

## Asset dell'admin

Il tema admin (Tabler e Tabler Icons) è in `admin-theme/` (ADR-0013). Le cartelle `tabler` e `tabler-icons` sono copiate per intero in `src/DvCms.Web/wwwroot/lib/` (A-004); le pagine HTML demo e gli altri formati non consentiti non sono serviti (404). Per aggiornare il tema si sostituiscono le due cartelle in `admin-theme/` e in `wwwroot/lib/`. Gli altri pacchetti di `admin-theme/` non vanno in `wwwroot`. Npm non è necessario per Tabler: `dist/` è già compilato.

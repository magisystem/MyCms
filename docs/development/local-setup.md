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
- Non committare password, connection string reali, API key, certificati o dati cliente.
- In sviluppo usare domini locali documentati oppure un middleware/test host esplicito; non introdurre bypass non sicuri nella configurazione produzione.
- Lo storage file system è esclusivamente un adapter per lo sviluppo.

## Flusso previsto dopo il bootstrap

1. Ripristinare i pacchetti NuGet.
2. Avviare SQL Server.
3. Configurare la connection string tramite user-secrets o environment variables.
4. Applicare le migrazioni EF Core con il comando documentato dalla solution.
5. Creare il primo `PlatformAdmin` con il comando di bootstrap documentato (nessuna credenziale predefinita).
6. Avviare `Cms.Web`.
7. Eseguire test unitari e d'integrazione.
8. Per il builder, entrare in `frontend/page-builder`, installare le dipendenze dal lockfile e avviare/buildare l'asset secondo lo script documentato.

Gli agenti non devono inventare comandi finali prima che i file di progetto siano stati creati. Aggiornare questa pagina nello stesso PR del bootstrap.


Per i dettagli su migrazioni e test, consultare [Database SQL Server](../architecture/database.md).

## Asset dell'admin

Il tema admin (Tabler e Tabler Icons) è in `admin-theme/` come sorgente di riferimento (ADR-0013). Al bootstrap si copiano in `wwwroot` solo gli asset usati; non servire direttamente la cartella. Npm non è necessario per Tabler: `dist/` è già compilato.

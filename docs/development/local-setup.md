# Setup di sviluppo locale

Questo file descrive il target di setup; i comandi precisi vanno completati durante il bootstrap, una volta fissati nomi progetto e tool test.

## Prerequisiti

- .NET 10 SDK aggiornato.
- SQL Server come requisito locale predefinito; PostgreSQL va avviato quando si lavora sulla compatibilità del secondo provider. Usare versioni supportate dai rispettivi provider EF Core.
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
2. Avviare SQL Server (default) oppure PostgreSQL se si sta verificando il provider secondario.
3. Configurare `Database:Provider` e la connection string corrispondente (`SqlServer` di default oppure `PostgreSql`).
4. Applicare la serie di migrazioni associata al provider selezionato tramite il comando documentato dalla solution. Non applicare migrazioni di un provider a un database dell'altro provider.
5. Avviare `Cms.Web`.
6. Eseguire test unitari e d'integrazione.
7. Per il builder, entrare in `frontend/page-builder`, installare le dipendenze dal lockfile e avviare/buildare l'asset secondo lo script documentato.

Gli agenti non devono inventare comandi finali prima che i file di progetto siano stati creati. Aggiornare questa pagina nello stesso PR del bootstrap.


Per i dettagli sulla doppia serie di migrazioni e sui test cross-provider, consultare [Strategia provider database](../architecture/database-providers.md).

# Convenzioni di codice

Convenzioni operative del progetto, verificate dove possibile da build, analyzer, `dotnet format` e test. Le decisioni di fondo sono negli ADR (in particolare ADR-0017 sulla denominazione e ADR-0018 sulla localizzazione).

## Denominazione

Tutto ciò che è nostro è in italiano (ADR-0017); restano in inglese i nomi dei progetti, del framework e delle librerie. Un'eccezione pratica: i metodi che sovrascrivono (`override`) o implementano un'API del framework mantengono i nomi dei parametri e dei metodi originali (ad esempio `InvokeAsync` del middleware, `ConfigureConventions(ModelConfigurationBuilder configurationBuilder)`), perché l'analyzer CA1725 lo richiede.

Stile (`.editorconfig`): namespace con ambito di file, parentesi graffe sempre, campi privati con prefisso `_`, costanti in PascalCase. `dotnet format --verify-no-changes` è parte della CI.

## Nullable e analyzer

`Nullable` è abilitato in tutti i progetti. `Directory.Build.props` imposta `AnalysisLevel=latest-recommended` e tratta gli avvisi come errori: un avviso non si lascia, si corregge o si sopprime in modo mirato e motivato. Le migrazioni EF Core generate (`Persistenza/Migrazioni`) sono escluse dagli analyzer.

## Tempo e UTC

- Tutti gli istanti sono **UTC**. Nel codice si usa `TimeProvider` (iniettato, `GetUtcNow()`), mai `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today` o gli equivalenti `DateTimeOffset`: un test (`ConvenzioniCodiceTest`) fallisce se compaiono in `src/`.
- Nel database gli istanti sono `datetime2` e il convertitore `ConvertitoreDataOraUtc` rifiuta i valori senza informazione sul fuso orario (`DateTimeKind.Unspecified`), converte quelli locali in UTC e restituisce sempre valori UTC.
- Le proprietà degli istanti UTC terminano con `Utc` (`CreatoIlUtc`). Il fuso orario del sito è un dato separato, usato solo per la presentazione.

## Identificatori

Gli identificatori delle entità sono `Guid` generati dall'applicazione con `Identificativi.Nuovo(TimeProvider)` (versione 7, ordinabile nel tempo). Non si usano chiavi numeriche autogenerate dal database.

## Importi

`decimal` con precisione `(18,4)` per convenzione del modello e valuta sempre esplicita accanto all'importo.

## Logging

- Log strutturato in JSON su console (formattatore `json` del framework, istanti UTC) in produzione; formato leggibile in `Development`.
- I messaggi usano segnaposto con nome (`{Numero}`) e il generatore `[LoggerMessage]`, non l'interpolazione di stringhe (analyzer CA1848/CA2254).
- Mai segreti, password, token, OTP o dati di pagamento nei log (AGENTS.md).

## Identificatore di correlazione

`MiddlewareIdCorrelazione` assegna a ogni richiesta un identificatore: accetta l'intestazione `X-Correlation-Id` solo se ha forma sicura (`[A-Za-z0-9_-]`, da 1 a 64 caratteri), altrimenti ne genera uno nuovo (Guid versione 7). L'identificatore è restituito nella risposta, diventa `HttpContext.TraceIdentifier` ed è presente in ogni riga di log della richiesta con la chiave `IdCorrelazione`. Il tenant (`IdTenant`) si aggiungerà allo stesso ambito di log con B-002.

## Configurazione e segreti

Le chiavi di configurazione sono in italiano (`ConnectionStrings:Predefinita`). I valori con segreti non stanno in `appsettings.json`: `appsettings.Local.json` (non versionato, escluso dalla pubblicazione), user-secrets in sviluppo, variabili d'ambiente o file di ambiente non versionati altrove. L'applicazione non parte se manca la stringa di connessione.

## Database e migrazioni

- Una sola serie di migrazioni EF Core in `src/DvCms.SqlServer/Migrazioni` (ADR-0024), con tabella di storico `StoricoMigrazioni`.
- Creare una migrazione: `dotnet tool restore` e poi `dotnet ef migrations add NomeMigrazione --project src/DvCms.SqlServer --startup-project src/DvCms.SqlServer --output-dir Migrazioni`. Revisionare sempre il codice generato.
- Applicarle: `dotnet run --project src/DvCms.Web -- migra` (comando `migra`, mai in automatico all'avvio).
- Un test di integrazione fallisce se il modello ha modifiche senza migrazione, e un altro applica tutte le migrazioni a un database vuoto.

## Test

xUnit v3 con Microsoft Testing Platform. I test sul database usano SQL Server reale indicato da `DVCMS_CONNESSIONE_TEST` (stringa di connessione a un server con permesso di creare ed eliminare database): ogni test crea un database temporaneo. Senza la variabile i test sul database risultano **saltati** (non passati); con `CI=true` falliscono.

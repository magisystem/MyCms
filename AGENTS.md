# Istruzioni per agenti di coding

Queste istruzioni si applicano a tutto il repository. I documenti `docs/` e gli ADR sono la fonte di verità del progetto; in caso di conflitto, non scegliere tacitamente: spiegare il conflitto e proporre una modifica documentata.

## Missione

Implementare un CMS SaaS multi-tenant in C#/.NET, con rendering server-side Razor, amministrazione server-rendered, page builder visuale isolato e modulo e-commerce interno. Privilegiare semplicità, sicurezza dei tenant, testabilità e manutenzione a lungo termine.

## Regole non negoziabili

1. **Non introdurre React, Angular, Blazor WebAssembly o un backend Node.js** senza una decisione architetturale approvata. L'admin standard è Razor Pages. HTMX è opzionale, non una dipendenza globale.
2. L'unico bundle JavaScript complesso previsto è il page builder GrapesJS e le sue estensioni. Il suo uso non cambia il backend, che resta ASP.NET Core.
3. Usare .NET 10 / ASP.NET Core 10 / EF Core 10, mantenendo le patch aggiornate. Non mescolare major version di EF Core e provider.
3a. SQL Server è il provider primario/predefinito; PostgreSQL è un provider supportato. Mantenere due serie distinte di migration EF Core e test reali per entrambi. Ogni cambiamento di schema aggiorna entrambe le serie nello stesso PR salvo deroga approvata. Vedi `docs/architecture/database-providers.md` e ADR-0003.
3b. Non fare affidamento su tipi, collazioni, funzioni SQL, indicizzazione JSON o comportamento transazionale specifici di un provider senza isolare la differenza e testarla su entrambi. EF Core InMemory/SQLite non sostituiscono test reali dei provider.
4. Implementare un **monolite modulare**. Non introdurre microservizi, broker o infrastrutture distribuite senza requisito misurabile e ADR.
5. Ogni query e operazione su dati tenant-scoped deve derivare da un contesto tenant attendibile e applicare autorizzazione e isolamento lato server. Non fidarsi di `TenantId` inviati dal browser.
6. I test di isolamento tenant sono obbligatori per ogni nuova area dati multi-tenant.
7. I contenuti salvati sono dati, mai codice eseguibile. Non consentire upload o caricamento dinamico di assembly, viste Razor, C# o JavaScript arbitrario sul server.
8. Non usare HTML/CSS esportati come unica fonte di verità per il progetto editabile. Conservare i dati del progetto GrapesJS e un documento CMS di rendering tipizzato/versionato secondo `docs/architecture/page-builder.md`.
9. I temi sono codice fidato compilato e distribuito con l'app; i tenant possono configurare impostazioni dichiarative, non caricare Razor o assembly.
10. Non memorizzare dati carta. Non inventare comportamento di pagamento, IVA, spedizioni o rimborsi prima delle decisioni di prodotto e del provider.
11. Non aggiungere dipendenze NuGet/npm senza spiegare perché sono necessarie, verificare licenza e manutenzione e limitarle al modulo che le richiede.
12. Non modificare un ADR accettato in silenzio. Proporre un nuovo ADR o aggiornare quello esistente con motivazione, alternative e conseguenze.

## Processo di lavoro richiesto

Per ogni task:

1. Leggi le sezioni pertinenti di `docs/` e gli ADR correlati.
2. Esamina il codice già presente prima di creare o rinominare progetti.
3. Riassumi brevemente piano, file che cambieranno, rischio e test previsti.
4. Implementa una sola unità verticale/coerente; non generare tutto il CMS in un'unica passata.
5. Esegui i test e i controlli realmente disponibili. Non dichiarare test eseguiti se non lo sono.
6. Aggiorna documentazione, migrazioni e configurazioni necessarie.
7. Concludi con: cosa è stato fatto, comandi test eseguiti, risultati, limiti residui e prossima attività raccomandata.

Se mancano dettagli non bloccanti, adotta la scelta più semplice compatibile con questi documenti e annota l'assunzione. Se una decisione ha impatti di sicurezza, dati, costi commerciali o migrazioni, fermati e presenta la decisione da approvare.

## Convenzioni tecniche

- C# con nullable reference types abilitati, async/await per I/O, cancellazione tramite `CancellationToken` per operazioni asincrone appropriate.
- Preferire tipi espliciti, nomi di dominio chiari e classi piccole con responsabilità definite.
- Non mettere logica di dominio in PageModel, controller, Razor view o JavaScript.
- Usare `decimal` per importi monetari e conservare la valuta esplicitamente.
- Non comporre SQL con input dell'utente. Parametrizzare le query.
- Non registrare segreti, token, dati di pagamento o contenuti personali sensibili nei log.
- Usare UTC per istanti persistiti; conservare separatamente il fuso orario del sito quando necessario.
- Le viste Razor non devono contenere query DB o logica di autorizzazione.
- Evitare repository generici che duplicano EF Core. Introdurre astrazioni solo quando rappresentano un confine utile.
- Non aggiungere pacchetti per funzionalità già coperte dal framework senza un motivo documentato.

## Prima milestone di implementazione

La prima implementazione deve essere una **vertical slice tenant + sito + dominio + pagina statica pubblicata**, con:

- solution compilabile;
- contesto tenant risolto da host registrato;
- isolamento tenant applicato sul server;
- una migrazione DB;
- pagina amministrativa Razor protetta per creare/modificare una pagina di prova;
- rendering pubblico Razor di una pagina pubblicata;
- test automatici che provino accesso consentito e tentativo di accesso cross-tenant negato.

Non implementare subito l'e-commerce o il builder completo. Prima dimostrare che il confine tenant e il flusso di pubblicazione sono solidi.

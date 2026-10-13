# Istruzioni per agenti di coding

Queste istruzioni si applicano a tutto il repository. I documenti `docs/` e gli ADR sono la fonte di verità del progetto; in caso di conflitto, non scegliere tacitamente: spiegare il conflitto e proporre una modifica documentata.

## Missione

Implementare un CMS SaaS multi-tenant in C#/.NET, con rendering server-side Razor, amministrazione server-rendered, page builder visuale isolato e modulo e-commerce interno. Privilegiare semplicità, sicurezza dei tenant, testabilità e manutenzione a lungo termine.

## Regole non negoziabili

1. **Non introdurre React, Angular, Blazor WebAssembly o un backend Node.js** senza una decisione architetturale approvata. L'admin standard è Razor Pages. HTMX è opzionale, non una dipendenza globale.
2. L'unico bundle JavaScript complesso previsto è il page builder GrapesJS e le sue estensioni. Il suo uso non cambia il backend, che resta ASP.NET Core.
3. Usare .NET 10 / ASP.NET Core 10 / EF Core 10, mantenendo le patch aggiornate. Non mescolare major version di EF Core e provider.
3a. SQL Server è l'unico database supportato (ADR-0012). Una sola serie di migration EF Core. Non aggiungere PostgreSQL o altri provider senza un nuovo ADR. Vedi `docs/architecture/database.md`.
3b. Il codice specifico di SQL Server (provider, migrazioni, hint, SQL raw, rilevamento dei deadlock) resta in `DvCms.SqlServer`, dietro interfacce di `DvCms.Contracts`, parametrizzato e testato; `DvCms.Data` non conosce nessun provider. `DvCms.PostgreSql` è un segnaposto vuoto (ADR-0024). EF Core InMemory/SQLite non sostituiscono test reali su SQL Server.
3c. **Struttura dei progetti (ADR-0024):** `Extender` ← `Models` ← `DTO` ← `Contracts` ← `Core` ← `Data` ← `Emails`; `Services`, `Services.Cms` e `Services.Negozio` sono indipendenti fra loro; `Web` conosce tutti. Un progetto dipende solo da quelli a sinistra e un test di architettura lo impone. `Models` non dipende da EF Core né da altro. I DTO si usano così come sono: le entità non si legano mai ai moduli né si mostrano alla UI con campi sensibili.
4. Implementare un **monolite modulare**. Non introdurre microservizi, broker o infrastrutture distribuite senza requisito misurabile e ADR.
5. Ogni query e operazione su dati tenant-scoped deve derivare da un contesto tenant attendibile e applicare autorizzazione e isolamento lato server. Non fidarsi di `IdTenant` inviati dal browser.
6. I test di isolamento tenant sono obbligatori per ogni nuova area dati multi-tenant.
7. I contenuti salvati sono dati, mai codice eseguibile. Non consentire upload o caricamento dinamico di assembly, viste Razor, C# o JavaScript arbitrario sul server. I modelli email nel database sono Liquid (Fluid) in sandbox, mai Razor ([ADR-0025](docs/decisions/ADR-0025-modelli-email.md)).
8. Non usare HTML/CSS esportati come unica fonte di verità per il progetto editabile. Conservare i dati del progetto GrapesJS e un documento CMS di rendering tipizzato/versionato secondo `docs/architecture/page-builder.md`.
9. I temi sono codice fidato compilato e distribuito con l'app; i tenant possono configurare impostazioni dichiarative, non caricare Razor o assembly.
10. Non memorizzare dati carta. Non inventare comportamento di pagamento, IVA, spedizioni o rimborsi prima delle decisioni di prodotto e del provider.
11. Non aggiungere dipendenze NuGet/npm senza spiegare perché sono necessarie, verificare licenza e manutenzione e limitarle al modulo che le richiede.
12. Non modificare un ADR accettato in silenzio. Proporre un nuovo ADR o aggiornare quello esistente con motivazione, alternative e conseguenze.
13. I ruoli sono `PlatformAdmin`, `TenantAdmin`, `Utente` (acquirente dell'e-commerce, solo emissione ordini) e `Guest` (visitatore anonimo vincolato a un tenant) (ADR-0011). Solo il `PlatformAdmin` può creare o elevare utenti `TenantAdmin`; nessun ruolo può assegnarsi un ruolo superiore. Controllo lato server con test di non-escalation. La registrazione esiste solo per gli `Utente` dei siti con e-commerce; mai per account amministrativi.
14. L'admin usa **Tabler** e **Tabler Icons** da `admin-theme/` (ADR-0013): al bootstrap le cartelle `tabler` e `tabler-icons` si copiano per intero in `wwwroot/lib/`, senza servire le pagine HTML demo. Non copiare in `wwwroot` né usare `tabler-emails`, `tabler-illustrations` e `tabler-avatars` senza una decisione esplicita. Non servire direttamente `admin-theme/`.
15. La verifica dei domini è manuale e i certificati TLS sono dell'hosting tramite reverse proxy (ADR-0014): non implementare ACME né verifica DNS automatica. L'admin sta sotto `/admin` di ogni host; in `Development` `localhost` (qualunque porta) risolve il tenant di gestione `cms.davidemartelli.dev`, mai in produzione. In sviluppo solo HTTP.
16. Il nome del progetto è **DvCms**: solution `DvCms.sln`, namespace e progetti con prefisso `DvCms.` (es. `DvCms.Web`, `DvCms.Models`, `DvCms.Data`, `DvCms.Services`; elenco e dipendenze nell'ADR-0024). **Tutto è in italiano** (ADR-0017): tabelle, colonne, classi, proprietà, metodi, funzioni, variabili, namespace interni, chiavi di configurazione, commenti e messaggi. **I nomi dei progetti restano in inglese** (`DvCms.Models`, `DvCms.Core`, ...). Restano in inglese anche API e nomi di framework e librerie, nomi di prodotti, i ruoli `PlatformAdmin`/`TenantAdmin`/`Utente`/`Guest`, `Tenant`, `Slug` e il comando `seed`.
17. Il tenant è deciso solo dall'host dell'URL e solo quel `IdTenant` va alle query; mai un tenant proveniente dal client. Un account non può accedere dall'host di un altro tenant (salvo il `PlatformAdmin`). Non esiste un dominio centrale di amministrazione.
18. Recupero password (solo `TenantAdmin` e `Utente`, non `PlatformAdmin`): prima email con codice OTP, seconda email distinta con link monouso (ADR-0011). La two-factor authentication è prevista ma va implementata per ultima, non è bloccante per il go-live e non va anticipata.
19. Le email si inviano solo tramite il contratto unico e l'outbox, con adapter Microsoft Graph, Gmail API, SendGrid e SMTP. Il provider è un campo del tenant, configurato dal `PlatformAdmin` con una UI che mostra solo i campi del provider scelto; i segreti sono cifrati nel database (ADR-0015).
20. Non si usa ASP.NET Core Identity (ADR-0016): account nella tabella `Utenze` propria, cookie authentication del framework, hash password con BCrypt (`BCrypt.Net-Next`, previa verifica di licenza e manutenzione), blocco di 5 minuti dopo 5 tentativi falliti, nessun algoritmo di hashing o crittografia inventato. Il tenant primario e il primo `PlatformAdmin` nascono dal seeding idempotente (ADR-0014): dati da configurazione, password da segreto, mai valori predefiniti.
21. Un tenant può avere più `TenantAdmin`, ma ogni account `TenantAdmin` o `Utente` appartiene a un solo tenant; la stessa email può esistere su tenant diversi come account distinti. Il `TenantAdmin` accede solo al proprio tenant; il `PlatformAdmin` entra in qualunque tenant con i poteri di un `TenantAdmin` più il menu Platform.
22. I nomi delle tabelle del database sono in italiano, plurale e PascalCase, mappati esplicitamente (`ToTable`); colonne e proprietà in italiano (chiavi esterne `Id...`, istanti UTC `...Utc`). L'elenco è in `docs/architecture/domain-model.md`.
23. Localizzazione (ADR-0018): lingue italiano (base) e inglese. Ogni testo statico visibile passa da `@_L["TESTO"]` (o dal localizzatore nel codice) con testo letterale in italiano; mai dati dell'utente. I testi statici stanno nel database (`TestiStatici`), sono globali e li modifica solo il `PlatformAdmin`. I testi dinamici sono per lingua: una `Pagina` per lingua collegata da `IdGruppoTraduzione`, tabelle `Traduzioni<Entità>` per le altre entità.

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
- una migrazione DB (SQL Server);
- login con account locali propri (senza ASP.NET Core Identity, ADR-0016), seeding idempotente del tenant primario e del primo `PlatformAdmin` senza credenziali predefinite (dati da configurazione, password da segreto) e un `TenantAdmin` per tenant;
- menu "Platform" nell'admin (solo `PlatformAdmin`) per creare un tenant, un sito, un dominio (verificato a mano) e un utente `TenantAdmin`, con ingresso nell'admin di un tenant auditato;
- pagina amministrativa Razor protetta (layout Tabler) per creare/modificare una pagina di prova;
- rendering pubblico Razor di una pagina pubblicata;
- test automatici che provino accesso consentito, tentativo di accesso cross-tenant negato e tentativi di escalation di ruolo respinti. Il ruolo `Utente` e la registrazione arrivano con l'e-commerce (M7).

Non implementare subito l'e-commerce o il builder completo. Prima dimostrare che il confine tenant e il flusso di pubblicazione sono solidi.

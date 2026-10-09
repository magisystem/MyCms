# Sicurezza

La sicurezza è un requisito di progetto, con particolare attenzione a isolamento tenant, editor visuale, file caricati e operazioni di pagamento.

## Identità e autorizzazione

- Account locali con tabelle e gestione proprie, senza ASP.NET Core Identity (ADR-0016): password con BCrypt (work factor 12 configurabile, pre-hash SHA-384, lunghezza massima limitata, rehash al login), cookie authentication del framework con `TimbroSicurezza` verificato a ogni richiesta, blocco di 5 minuti dopo 5 tentativi falliti consecutivi, rate limiting e errore uniforme al login. Cookie sicuri per l'admin. Nessuna registrazione self-service per account amministrativi; la registrazione degli acquirenti esiste solo nei siti con e-commerce (ADR-0011, ADR-0014).
- Il `TenantAdmin` accede solo al proprio tenant; il `PlatformAdmin` a tutti, con i poteri del `TenantAdmin` più il menu Platform.
- Autorizzare ogni operazione in base a identità, tenant dell'account, sito, ruolo e risorsa.
- Ruoli: `PlatformAdmin` (globale), `TenantAdmin` (un solo tenant, più `TenantAdmin` per tenant), `Utente` (acquirente di un tenant, solo ordini), `Guest` (anonimo, sempre vincolato a un tenant).
- Solo il `PlatformAdmin` crea o eleva `TenantAdmin`; nessun ruolo assegna un ruolo superiore al proprio né modifica il proprio. La registrazione crea solo `Utente`; il ruolo non è mai preso dalla richiesta. Controllo lato server con test di non-escalation.
- Il primo `PlatformAdmin` si crea con il seeding del tenant primario (ADR-0014), con password da segreto o variabile d'ambiente, mai da file versionati e senza credenziali predefinite.
- Ingresso del `PlatformAdmin` nell'admin di un tenant e azioni del menu "Platform" sono registrati in `RegistroAudit`. L'ingresso è l'accesso (l'autenticazione è per host): dal dettaglio del tenant il link "Apri l'admin" porta sul suo dominio, dove l'accesso del `PlatformAdmin` registra `AccessoPlatformAdmin`.
- **Menu "Platform" (area Razor `Platform`, percorso `/admin/platform`).** Protetta da una politica che richiede il ruolo `PlatformAdmin` (403 per il `TenantAdmin`, reindirizzamento all'accesso se anonimo); il menu è nascosto agli altri ruoli, ma è solo comodità. Difesa in profondità: `IServizioPiattaforma` riverifica sul database, a ogni operazione, che l'attore sia un `PlatformAdmin` attivo (non si fida di claim né di campi del client); le scritture negate restituiscono `NonAutorizzato` e sono registrate come `OperazioneNegata`, le letture negate lanciano `UnauthorizedAccessException`. Ogni scrittura riuscita è nella stessa transazione della sua voce di audit.
- **Credenziali iniziali del `TenantAdmin` (M1).** Il `PlatformAdmin` crea l'account: il sistema genera una password casuale di 20 caratteri (generatore crittografico, alfabeto senza caratteri ambigui) mostrata una sola volta nella risposta della creazione (nessun reindirizzamento, nessun `TempData`, `Cache-Control: no-store`); non è salvata in chiaro, né nei log né nell'audit; l'account ha `DeveCambiarePassword` e ogni pagina dell'admin, area Platform compresa, lo rimanda al cambio password. L'invito via email sostituirà questo passaggio in M1b. Non si può creare un `TenantAdmin` con l'email di un `PlatformAdmin` (ambiguità nel login).
- **Elevazione.** Solo un `Utente` attivo del tenant può essere elevato a `TenantAdmin`, dal `PlatformAdmin`; il timbro di sicurezza cambia e le sessioni esistenti si invalidano. Un account non si eleva da solo. Disabilitare un account del tenant (mai un `PlatformAdmin`) cambia il timbro e termina le sue sessioni.
- **Domini inseriti dal menu.** Devono essere nomi DNS con almeno un punto (mai `localhost` né indirizzi IP, che arrivano solo dal popolamento in sviluppo). Un dominio nasce verificato solo se l'operatore dichiara di averne verificato il possesso; chi e quando sono conservati in `DominiSito`.
- **Recupero password** (`TenantAdmin` e `Utente`; il `PlatformAdmin` non lo ha, la sua password è gestita a mano dall'operatore): account cercato nel tenant dell'host; risposta identica a prescindere dall'esistenza dell'account; **prima email** con codice OTP (6 cifre, 10 minuti, 5 tentativi, salvato come hash), **seconda email distinta** con link monouso (30 minuti), invalidato all'uso e da nuove richieste; rate limiting (3 richieste/ora per account e IP); al cambio password sessioni e token sono invalidati e arriva una notifica. I valori sono iniziali e configurabili (ADR-0011).
- **Two-factor authentication:** requisito previsto, a priorità minima, da implementare per ultimo e non bloccante per il go-live (ADR-0011). Metodo e obbligatorietà per `PlatformAdmin` e `TenantAdmin` saranno decisi con un ADR.
- **Registrazione acquirenti:** solo su siti con e-commerce attivo; conferma email, rate limiting e protezione anti-abuso.
- Separare privilegi globali da ruoli tenant.
- Proteggere form mutanti con antiforgery; applicare policy session/cookie appropriate.
- Evitare IDOR: conoscere un GUID non equivale ad avere accesso.

## Dati tenant-scoped

- Tenant context derivato solo dall'host dell'URL, non dal payload né da campi del client; l'account deve appartenere a quel tenant (salvo il `PlatformAdmin`).
- Admin e sito pubblico condividono l'origine dello stesso host: schemi e cookie di autenticazione distinti per admin e area cliente, cookie admin con percorso limitato, e CSP e sanitizzazione del rich text sono controlli essenziali (un XSS sul sito pubblico raggiunge l'origine dell'admin).
- Validazione tenant sia a livello application sia persistence.
- Foreign key composite e test cross-tenant.
- Accessi di supporto cross-tenant espliciti, privilegiati e auditati.
- Chiavi di cache con tenant/site e nessuna condivisione accidentale di asset privati.

## Editor e contenuti non attendibili

- Non eseguire codice C#, assembly, file Razor o JavaScript fornito da tenant.
- Usare registry allow-list per componenti e renderer.
- Validare schema/versione, profondità albero, dimensione payload, tipi e proprietà.
- Sanitizzare rich text HTML con una libreria/policy mantenuta; non usare regex come unico sanitizer.
- Rifiutare script, event handler inline, URL javascript: e attributi pericolosi.
- CSP e cookie/security headers da definire e verificare in deployment.
- Separare il contesto di preview dall'admin se possibile, usando un origin isolato o token scoped di breve durata.

## Upload

- Limiti dimensione, estensioni allow-list, MIME e signature sniffing.
- Generare object key server-side; non usare path fornito dall'utente.
- Scansione malware dove prevista e gestione stato quarantena.
- Proteggere download privati con autorizzazione; gli asset pubblici possono avere URL CDN.
- Non servire HTML/SVG non affidabile come contenuto attivo senza una policy specifica.

## Web e API

- HTTPS obbligatorio in produzione, terminato dall'hosting sul reverse proxy; l'app dietro il proxy accetta gli header forwarded solo dai proxy configurati e i cookie di autenticazione sono `Secure` (la politica meno restrittiva è ammessa solo in sviluppo, dove si usa solo HTTP).
- Rate limiting sugli endpoint sensibili.
- Protezione dei proxy forwarded configurando proxy noti.
- Validazione dei redirect e delle URL esterne per prevenire open redirect/SSRF.
- Error page pubbliche senza stack trace; log interni senza segreti.
- Dipendenze aggiornate e analisi vulnerabilità in CI.

## Pagamenti

- Nessun dato carta persistito nella piattaforma.
- Verificare firma webhook, provider account e correlazione ordine/tenant.
- Idempotency key e deduplicazione eventi.
- Audit su transizioni finanziarie; non loggare payload sensibili.

## Localizzazione

I testi statici (`TestiStatici`) sono globali e li modifica solo il `PlatformAdmin`, con voce in `RegistroAudit`. L'output del localizzatore è codificato in HTML da Razor e i testi sono testo semplice; al localizzatore si passano solo costanti letterali, mai dati di utenti o visitatori (altrimenti si creerebbero righe senza limite). Nelle chiavi e nei testi non vanno segreti o dati personali.

## Email

L'invio passa da adapter intercambiabili (Microsoft Graph, Gmail, SendGrid, SMTP) via outbox (ADR-0015). Impostazioni e segreti dei canali sono per tenant, cifrati nel database, in sola scrittura nella UI e fuori dal repository e dai log; OTP, token e link non vanno nei log; mittente con SPF/DKIM/DMARC; template email non modificabili dai tenant.

## Privacy e audit

Minimizzare dati personali, definire retention ed export/delete secondo requisiti applicabili. L'implementazione finale di privacy, consensi, DPA e policy di conservazione richiede una verifica legale del mercato target; l'agente non deve inventare conformità.

## Security gate prima del go-live

- Threat model aggiornato.
- Test isolamento tenant per CRUD/API/media/cache/jobs/commerce.
- Test XSS, CSRF, IDOR, upload e webhook.
- Segreti gestiti fuori dal repository.
- Backup/restore testati.
- Dipendenze con vulnerabilità note valutate.

## Gestione delle pagine (C-002)

- `/admin/pagine` è nell'area `/admin` (politica `AmministratoreTenant`, cambio password obbligatorio, antiforgery su ogni scrittura). `IServizioPagine` lavora solo sul tenant e sul sito risolti dall'host: nessun identificativo di tenant o sito arriva dal client, e una pagina di un altro tenant o di un altro sito è come inesistente (404). L'attore è riverificato sul database a ogni operazione (`TenantAdmin` di quel tenant oppure `PlatformAdmin`, account attivo); le scritture negate sono registrate come `OperazioneNegata`, le letture lanciano `UnauthorizedAccessException`.
- Il contenuto M1 è testo semplice, mostrato sempre codificato (Razor): nessun HTML dell'utente viene emesso. Il rich text, con sanitizzazione e CSP, arriva con M2.
- Le modifiche concorrenti sono rilevate dal token `rowversion` della pagina (hidden field del modulo): chi salva con un token vecchio riceve un conflitto e non sovrascrive nulla. Lo slug di una pagina già pubblicata si può cambiare: il vecchio indirizzo resta come reindirizzamento 301 (C-005, vedi `publishing-and-seo.md`).

## Sito pubblico (C-003)

- `/` e `/{slug}` (una sola pagina Razor, `Pages/Pubblico/Pagina`) servono il sito dell'host risolto. `IServizioPaginePubbliche` legge solo la **revisione pubblicata** di pagine in stato `Pubblicata` con lingua attiva: bozze, modifiche non pubblicate, pagine ritirate e inesistenti non si distinguono (404 con pagina `noindex`). Gli slug non validi (punti, barre, percent-encoding che non decodifica in uno slug, spazi) danno 404 senza interrogare il database.
- Il tenant è deciso solo dall'host (vedi `multi-tenancy.md`); lo stesso slug in tenant o siti diversi mostra contenuti diversi e una pagina di un tenant non è raggiungibile dall'host di un altro. `/admin`, `/lib` e `/errore` sono riservati e hanno precedenza.
- Lo slug si cerca senza distinguere maiuscole (viene normalizzato in minuscolo). Se esistono più lingue con lo stesso slug vince la lingua corrente, poi quella predefinita, poi la prima per codice; il prefisso di lingua nel percorso arriva con C-008.
- Il contenuto è testo semplice sempre codificato; nessun HTML dell'utente viene emesso. Il layout pubblico non dipende dagli asset del tema admin (`/css/pubblico.css`).

## Configurazione email del tenant (B-009)

- La schermata `/admin/platform/tenant/{id}/email` è solo del `PlatformAdmin` (area Platform, 403 per gli altri; il servizio riverifica l'attore sul database). Mostra i soli campi del canale scelto (un'azione GET ricarica la pagina; un piccolo script esterno `canale-email.js` lo fa al cambio di selezione, compatibile con una CSP senza script inline) e la validazione è sempre lato server (`DefinizioniProviderEmail`).
- **Segreti in sola scrittura:** password, chiavi API e segreti client non si rileggono mai; la pagina mostra solo "impostato". Lasciare vuoto un segreto lo mantiene, scriverne uno lo sostituisce, una casella "Rimuovi" cancella quelli facoltativi. Cambiando canale i segreti del vecchio canale vengono scartati. Nell'audit finiscono il canale e i nomi dei segreti, mai i valori. Nei campi di un modulo rifiutato i segreti non vengono rimandati al browser.
- **Invio di prova:** passa dall'outbox come ogni email (mai nella richiesta web); l'esito si vede nell'elenco "Invii non riusciti" del tenant, con tentativi ed errore (senza segreti). Un tenant senza configurazione valida non invia e l'errore è visibile ai `PlatformAdmin`.
- Se manca la chiave dei segreti (`Sicurezza:ChiaveSegreti`) il salvataggio di un segreto è rifiutato con un messaggio che indica cosa configurare; un salvataggio senza segreti (ad esempio SMTP senza autenticazione) non la richiede.

## Recupero password (B-008)

- **Percorsi:** `/admin/recupero` (richiesta), `/admin/recupero/codice` (OTP) e `/admin/recupero/reimposta?token=...` (link), tutti anonimi, per il solo tenant dell'host. Valgono per `TenantAdmin` e `Utente` attivi di quel tenant; il `PlatformAdmin` non ha recupero (nessuna richiesta lo raggiunge, anche se l'email è la sua).
- **Fase 1 – richiesta:** qualunque email dà la stessa risposta (reindirizzamento alla pagina del codice e lo stesso cookie). Si registra sempre una riga in `RichiesteRecuperoPassword`; solo se l'account è ammesso e i limiti lo permettono parte la **prima email** con un codice di 6 cifre (generatore crittografico). Le altre (account inesistente, disabilitato, email non valida, limite superato) sono righe `Ignorata`: servono ai limiti e a non distinguersi dalle reali.
- **Limiti** (iniziali di ADR-0011, configurabili in `Sicurezza:RecuperoPassword:*`): codice valido 10 minuti, 5 tentativi per richiesta (poi la richiesta è annullata anche se si indovina), link valido 30 minuti, 3 richieste all'ora per account (tenant + email, esista o no) e 10 all'ora per indirizzo IP; in più il limite di richieste al minuto per IP delle pagine di accesso.
- **Fase 2 – codice:** il browser è legato alla richiesta da un cookie `.DvCms.Recupero` (riferimento casuale a 256 bit; sul server c'è solo l'hash), HttpOnly, SameSite Strict, `Path=/admin/recupero`, 30 minuti, Secure fuori da sviluppo. Con il codice giusto parte la **seconda email**, distinta, con il link monouso; il link non viene mai mostrato a schermo. Per le richieste `Ignorata` il codice non può mai essere giusto, e i messaggi sono identici.
- **Fase 3 – link:** il token è 256 bit casuali, salvato solo come hash SHA-256; la pagina risponde `Referrer-Policy: no-referrer` e `Cache-Control: no-store` e la modifica avviene con una POST con antiforgery. Un link scaduto, già usato, inventato, di un altro tenant o di un account nel frattempo disabilitato dà sempre lo stesso errore generico. Una password non valida o una conferma diversa non consumano il link.
- **Effetti della reimpostazione:** nuova password (BCrypt, politica 12–128 caratteri), `TimbroSicurezza` rinnovato (le sessioni esistenti decadono), blocco per tentativi falliti azzerato, link e tutte le altre richieste aperte dell'account invalidati, **notifica email** del cambio, voce di audit `RecuperoPasswordCompletato` senza segreti. Una nuova richiesta invalida codice e link delle precedenti. L'uso concorrente dello stesso link riesce una sola volta.
- **Segreti:** nel database ci sono solo gli hash di codice (con sale), riferimento e token. Le due email di recupero sono messaggi outbox *riservati*: anche il loro contenuto (codice, link) è cifrato nella tabella `MessaggiOutbox`. L'invio avviene nel worker; un tenant senza email configurata non invia e il problema compare nella schermata email del tenant, mentre l'utente vede la risposta di sempre.
- **Limiti noti:** i tempi di risposta di richieste reali e non reali non sono identici al microsecondo (le reali accodano un'email cifrata); il lavoro sul database è però lo stesso e l'invio non è sincrono. Il codice a 6 cifre è salvato con un hash semplice con sale: protegge dalla lettura casuale, non da un attacco offline, ma vale solo 10 minuti. Le righe scadute non vengono ancora ripulite (G-001). L'invito via email al posto della password temporanea per i nuovi `TenantAdmin` (roadmap M1b) è ancora da fare.

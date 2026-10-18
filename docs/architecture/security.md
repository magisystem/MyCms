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
- **Credenziali iniziali del `TenantAdmin` (M1).** Il `PlatformAdmin` crea l'account: il sistema genera una password casuale di 20 caratteri (generatore crittografico, alfabeto senza caratteri ambigui) mostrata una sola volta nella risposta della creazione (nessun reindirizzamento, nessun `TempData`, `Cache-Control: no-store`); non è salvata in chiaro, né nei log né nell'audit; l'account ha `DeveCambiarePassword` e ogni pagina dell'admin, area Platform compresa, lo rimanda al cambio password. Se il tenant ha configurato il canale email, il `PlatformAdmin` può invece invitare il nuovo `TenantAdmin` (sezione "Inviti"): nessuna password viene comunicata. Non si può creare un `TenantAdmin` con l'email di un `PlatformAdmin` (ambiguità nel login).
- **Elevazione.** Solo un `Utente` attivo del tenant può essere elevato a `TenantAdmin`, dal `PlatformAdmin`; il timbro di sicurezza cambia e le sessioni esistenti si invalidano. Un account non si eleva da solo. Disabilitare un account del tenant (mai un `PlatformAdmin`) cambia il timbro e termina le sue sessioni.
- **Domini inseriti dal menu.** Devono essere nomi DNS con almeno un punto (mai `localhost` né indirizzi IP, che arrivano solo dal popolamento in sviluppo). Un dominio nasce verificato solo se l'operatore dichiara di averne verificato il possesso; chi e quando sono conservati in `DominiSito`.
- **Recupero password** (`TenantAdmin` e `Utente`; il `PlatformAdmin` non lo ha, la sua password è gestita a mano dall'operatore): account cercato nel tenant dell'host; risposta identica a prescindere dall'esistenza dell'account; **prima email** con codice OTP (6 cifre, 10 minuti, 5 tentativi, salvato come hash), **seconda email distinta** con link monouso (30 minuti), invalidato all'uso e da nuove richieste; rate limiting (3 richieste/ora per account e IP); al cambio password sessioni e token sono invalidati e arriva una notifica. I valori sono iniziali e configurabili (ADR-0011).
- **Two-factor authentication (G-006, ADR-0019):** vedi la sezione "Autenticazione a due fattori" più sotto.
- **Registrazione acquirenti:** solo su siti con e-commerce attivo; conferma email, rate limiting e protezione anti-abuso (prova di lavoro ALTCHA, [ADR-0026](../decisions/ADR-0026-altcha.md)).
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

**Attuazione (D-003, D-004).**

- **Ammessi solo JPEG, PNG, GIF e WebP.** Il server legge il contenuto (fino al limite, mai oltre in memoria), riconosce il formato dalla **firma** e ne legge le dimensioni dall'intestazione, senza decodificare l'immagine né usare librerie grafiche. L'estensione deve essere nell'elenco e coerente con il formato rilevato (un PNG chiamato `.jpg`, un HTML chiamato `.png`, `.svg`, `.html`, `.pdf`, doppie estensioni e file senza estensione sono rifiutati); il tipo dichiarato dal client si ignora. GIF e WebP malformati o troncati sono rifiutati (anche un testo che inizia per `GIF89a`). Gli errori dicono solo cosa non va (`File`, `Dimensione`, `Tipo`, `Dimensioni`), mai il contenuto.
- **Limiti (configurazione `Media:*`):** dimensione massima del file (10 MB), lato massimo dell'immagine (12.000 px) e numero massimo di pixel (50 milioni): le dimensioni dichiarate nell'intestazione si controllano prima di salvare, per non accettare file che diventerebbero bombe di decompressione alla prima trasformazione. Il limite di Kestrel per il corpo della richiesta (30 MB predefiniti) resta una soglia più alta: se si porta `Media:DimensioneMassimaKb` oltre, va alzato anche quello.
- **Nome e chiave:** il nome originale è ripulito (solo l'ultimo segmento dopo `/` o `\`, caratteri di controllo tolti, 200 caratteri) e serve da etichetta, sempre codificato in uscita; la chiave dell'oggetto è generata dal server e non contiene mai testo dell'utente.
- **Archivio:** `IArchivioOggetti` non conosce i tenant; li separa `IServizioMedia`, che genera la chiave `{tenant}/{sito}/{risorsa}` e controlla nel database tenant e sito dell'host (filtro globale) prima di leggere, scrivere o eliminare: un id di un altro tenant o sito è come inesistente (404, `NonTrovato`). L'adapter su file system accetta solo chiavi di segmenti `[A-Za-z0-9_-]` separati da `/` (niente punti, quindi niente `..`, né percorsi assoluti), verifica che il percorso risolto resti nella radice, scrive in modo atomico e non sovrascrive mai. Se il salvataggio sul database fallisce l'oggetto appena scritto si toglie; se la cancellazione dell'oggetto fallisce dopo quella dei metadati resta un file orfano non raggiungibile, segnalato nei log.
- **Download:** `/media/{id}` serve solo risorse **pubbliche** del sito dell'host (non in quarantena) con il tipo rilevato dal server, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Content-Disposition: inline`, `Cross-Origin-Resource-Policy: same-site`, ETag (l'impronta) e `Cache-Control: public, max-age=3600` (una risorsa resa privata o eliminata può restare in cache al più un'ora). Le risorse **private** si aprono solo da `/admin/media/{id}?handler=File`, sotto la sessione admin del tenant (il cookie dell'admin vale solo per `/admin`), con `Cache-Control: private, no-store`. Privata, in quarantena, inesistente e di un altro tenant sono indistinguibili dal sito pubblico (404).
- **Non ancora:** scansione antimalware (G-005), quote per tenant, trasformazioni/miniature, riferimenti dalle pagine (e quindi controllo "risorsa in uso" prima dell'eliminazione), SVG sanificato.

## Web e API

- HTTPS obbligatorio in produzione, terminato dall'hosting sul reverse proxy; l'app dietro il proxy accetta gli header forwarded solo dai proxy configurati e i cookie di autenticazione sono `Secure` (la politica meno restrittiva è ammessa solo in sviluppo, dove si usa solo HTTP).
- Rate limiting sugli endpoint sensibili (accesso, recupero, inviti): per IPv4 per indirizzo, per IPv6 per rete /64.
- **Form pubbliche (ALTCHA, [ADR-0026](../decisions/ADR-0026-altcha.md)):** registrazione degli acquirenti e richiesta di recupero password richiedono una prova di lavoro firmata dal server (sfida su `/altcha/sfida`, con limite di richieste per indirizzo), valida una volta sola. La chiave di firma è `Altcha:HmacSignatureSecret` (segreto di installazione, non nel repository); `Altcha:Abilitato=false` spegne la protezione.
- **Moduli di contatto ([ADR-0027](../decisions/ADR-0027-moduli-di-contatto.md)):** definiti dal `TenantAdmin` come dati (nessun HTML, script o espressione regolare libera). L'invio pubblico `POST /moduli/{id}` verifica in quest'ordine: protezione accesa (altrimenti 503 fuori da Development), modulo pubblicato nel sito dell'host (altrimenti 404), prova ALTCHA, tetto giornaliero per modulo (429), validazione contro la definizione corrente senza over-posting. Le richieste contengono dati personali: non si registrano IP né User-Agent, si conservano per i giorni scelti (30–1825) e si cancellano da sole; accesso, esportazione CSV (formule neutralizzate) e cancellazione solo al `TenantAdmin` con audit senza dati; le email che le riportano sono riservate (cifrate nell'outbox).
- **Intestazioni e CSP (G-005):** su ogni risposta `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy` e una CSP con solo risorse della stessa origine e nessuno script in linea; per l'admin anche `frame-ancestors 'none'`, `X-Frame-Options: DENY` e `Cache-Control: no-store`; HSTS fuori da Development. Dettagli e verifica in `docs/operations/revisione-sicurezza.md`.
- **Chiavi dei cookie e dell'antiforgery:** `Sicurezza:ChiaviCookie:Percorso` (cartella persistente e condivisa fra le istanze, permessi riservati: contiene chiavi in chiaro); senza, le sessioni non sopravvivono a un contenitore ricreato.
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

Stato della revisione G-005 (dettagli, problemi corretti e rischi accettati in `docs/operations/revisione-sicurezza.md`):

- [x] Threat model aggiornato.
- [x] Test isolamento tenant per CRUD/media/cache/jobs (commerce e API: da ripetere quando esisteranno).
- [x] Test XSS, CSRF, IDOR, upload (webhook: da ripetere con i pagamenti).
- [x] Segreti gestiti fuori dal repository.
- [x] Backup/restore testati (`docs/operations/backup-e-ripristino.md`).
- [x] Dipendenze con vulnerabilità note valutate, controllo automatico in CI.
- [ ] Penetration test esterno e verifica in produzione (elenco "Da rifare prima del go-live").

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

## Autenticazione a due fattori (G-006)

Decisioni in [ADR-0019](../decisions/ADR-0019-two-factor-authentication.md); procedure operative in [`docs/operations/runbook-due-fattori.md`](../operations/runbook-due-fattori.md).

- **Metodo:** TOTP RFC 6238 (HMAC-SHA1, 6 cifre, 30 secondi, tolleranza ±1 passo), implementato con la sola libreria di base (`Totp`, `Base32`). Il segreto (20 byte casuali) è cifrato a riposo con `IProtettoreSegreti` (`Sicurezza:ChiaveSegreti`) e non viene mai mostrato dopo la configurazione né scritto nei log. Il codice QR è un SVG generato sul server (`QRCoder`, licenza MIT, limitato a `DvCms.Infrastructure`), in linea nella pagina: nessuna immagine esterna e nessuno script (la CSP resta `script-src 'self'`).
- **Anti-replay:** `UltimoPassoTotp` e un aggiornamento atomico condizionato (`UPDATE ... WHERE UltimoPassoTotp < passo`): lo stesso codice non vale due volte nemmeno con richieste parallele (test con otto richieste simultanee).
- **Configurazione** (`/admin/sicurezza`, voce "Verifica in due passaggi" del menu utente): "Configura" genera un segreto *in attesa* e lo mostra una sola volta (QR e chiave testuale); la 2FA si attiva solo con un codice valido di quel segreto. All'attivazione il timbro di sicurezza cambia (le altre sessioni decadono), la sessione corrente si rinnova e compaiono 10 codici di recupero, una volta sola. Cambiare dispositivo, rigenerare i codici e disattivare richiedono **password e un codice**; i fallimenti contano sul contatore dei tentativi.
- **Codici di recupero:** 10 da 80 bit (`XXXX-XXXX-XXXX-XXXX`), salvati solo come hash SHA-256 in `CodiciRecuperoUtenza`, monouso con aggiornamento atomico (`UsatoIlUtc IS NULL`); l'uso è nell'audit (`UsoCodiceRecupero`, con il numero di codici rimasti). La rigenerazione invalida i precedenti.
- **Accesso in due passi:** la password corretta di un account con 2FA attiva **non crea alcuna sessione**: emette il cookie di sfida `.DvCms.SecondoFattore` (5 minuti, `HttpOnly`, `SameSite=Strict`, `Path=/admin`, cifrato con Data Protection, legato al tenant dell'host; contiene account, timbro e destinazione locale). `/admin/accesso/verifica` (anonima, con il limite di richieste degli accessi) accetta un codice TOTP o di recupero; il servizio riverifica account attivo, ruolo, tenant dell'host, timbro e stato di blocco sul database. Una sfida manomessa, scaduta, di un altro host o di un altro anello di chiavi è rifiutata. L'ingresso del `PlatformAdmin` si registra nell'audit solo a verifica completata.
- **Forza bruta:** un codice sbagliato incrementa lo **stesso contatore atomico** della password e porta allo stesso blocco (5 tentativi, 5 minuti). La password corretta *non* azzera il contatore per un account con 2FA: si azzera solo a secondo fattore verificato (altrimenti alternando password giusta e codice sbagliato si indovinerebbe il codice senza mai bloccarsi).
- **Obbligo:** `Sicurezza:DueFattori:Obbligo` = `Amministratori` (predefinito: `PlatformAdmin` e `TenantAdmin`), `PlatformAdmin` o `Nessuno`. Chi non l'ha configurata accede con la password ma ogni pagina dell'admin (area Platform compresa) lo rimanda a `/admin/sicurezza` (`FiltroDueFattoriObbligatorio`; il cambio della password temporanea ha la precedenza). Con l'obbligo attivo la 2FA non si disattiva. Fuori da Development `Nessuno` produce un avviso all'avvio, come l'obbligo senza `Sicurezza:ChiaveSegreti`. `Utente` (acquirente) è fuori ambito.
- **Recupero:** codici di recupero → reimpostazione da parte del `PlatformAdmin` per i `TenantAdmin` (dettaglio del tenant, "Reimposta 2FA": azzera segreto e codici, chiude le sessioni, audit `ReimpostazioneDueFattori`) → comando operativo `DvCms.Web reset-2fa <email>` per un `PlatformAdmin` (solo `PlatformAdmin`, audit senza attore). **Il recupero password non disattiva né aggira la 2FA.**
- **Rischi noti:** chi ha la password può far scattare il blocco di 5 minuti (come prima); la perdita di `Sicurezza:ChiaveSegreti` rende illeggibili i segreti TOTP (tutti gli account con 2FA vanno reimpostati); il confronto dei codici TOTP è a tempo costante ma l'esistenza di una sfida valida non è nascosta a chi ha già la password.

## Page builder (M4)

Decisioni in ADR-0005/0007; funzionamento in [`page-builder.md`](page-builder.md); valutazione in [`docs/delivery/valutazione-poc-m4.md`](../delivery/valutazione-poc-m4.md).

- **Il browser non è un'autorità.** Il server converte il progetto dell'editor in documento, lo valida (registro dei componenti: tipi, proprietà, stili, annidamento, limiti), lo confronta col template (nodi bloccati, slot) e verifica le risorse; ignora qualunque informazione di blocco inviata dal client. Il progetto memorizzato è rigenerato dal documento validato: ciò che il browser ha aggiunto e il documento non rappresenta non viene mai salvato né ricaricato nell'origine dell'admin.
- **Nessun contenuto eseguibile del tenant.** Il documento contiene testo semplice, enumerazioni, GUID di risorsa e indirizzi (`/percorso` o `https://`, mai `javascript:`, `data:`, `//host` o `http://`). Il renderer codifica ogni valore, ricontrolla indirizzi e token in uscita e ignora i tipi sconosciuti; un documento alterato direttamente nel database non produce HTML pericoloso. Il testo formattato (M5) non è HTML: è un markup a grammatica chiusa analizzato e riscritto dal server, con elementi di uscita fissi e valori codificati; non c'è nulla da sanitizzare e nessuna libreria da fidarsi. Indirizzi dei collegamenti con la stessa regola degli altri (`/percorso` o `https://`, ricontrollati in uscita). Grammatica verificata con vettori di prova comuni a .NET e all'editor.
- **Endpoint.** Pagina e salvataggio solo per `TenantAdmin` del tenant o `PlatformAdmin` (riverificati sul database), antiforgery obbligatorio (anche per il corpo JSON), limite di dimensione del corpo, risposte `no-store`, errori con codici stabili e senza valori. Un'altra pagina o un altro tenant danno 404. Le risorse media devono essere pubbliche e del sito (nessun riferimento a risorse di altri tenant o private).
- **Concorrenza.** Token della pagina a ogni salvataggio: nessuna sovrascrittura silenziosa.
- **Bundle JavaScript.** Compilato da sorgenti in `frontend/page-builder` con lockfile e versioni esatte, versionato e verificato in CI (`npm run verifica`, `npm audit`). Caricato solo dalla pagina del builder, dalla stessa origine; CSP invariata (`script-src 'self'`, nessun `unsafe-eval`, nessuna risorsa esterna). Contiene `new Function` (templating di underscore) che nei percorsi usati non viene eseguito: la CSP lo bloccherebbe; il test di fumo con browser vero controlla le violazioni a ogni aggiornamento di GrapesJS.
- **Animazioni, autosave e modifica nel canvas (M5).** Le animazioni sono due scelte da elenco: il renderer emette solo nomi di classe fissi e la logica sta in un file JavaScript del sito (`animazioni.js`), mai nel documento; rispettano `prefers-reduced-motion` e non nascondono contenuti senza JavaScript. Il salvataggio automatico passa dagli stessi controlli del manuale (token, policy, registro) e può sostituire solo la revisione automatica non pubblicata dello stesso autore (mai manuale, pubblicata o di altri). La modifica del testo nel canvas legge solo `textContent`, incolla solo testo semplice e rispetta le proprietà modificabili dei template; le scorciatoie che agiscono sui componenti sono disattivate.
- **Rischi noti:** `backbone-undo` (transitiva di GrapesJS) non più supportata, senza vulnerabilità note; accessibilità dell'editor (uso da tastiera attuato; mancano prova con screen reader reali e audit automatico); anteprima nello stesso origine dell'admin (testo sempre codificato, nessun rich text).

## Recupero password (B-008)

- **Percorsi:** `/admin/recupero` (richiesta), `/admin/recupero/codice` (OTP) e `/admin/recupero/reimposta?token=...` (link), tutti anonimi, per il solo tenant dell'host. Valgono per `TenantAdmin` e `Utente` attivi di quel tenant; il `PlatformAdmin` non ha recupero (nessuna richiesta lo raggiunge, anche se l'email è la sua).
- **Fase 1 – richiesta:** qualunque email dà la stessa risposta (reindirizzamento alla pagina del codice e lo stesso cookie). Si registra sempre una riga in `RichiesteRecuperoPassword`; solo se l'account è ammesso e i limiti lo permettono parte la **prima email** con un codice di 6 cifre (generatore crittografico). Le altre (account inesistente, disabilitato, email non valida, limite superato) sono righe `Ignorata`: servono ai limiti e a non distinguersi dalle reali.
- **Limiti** (iniziali di ADR-0011, configurabili in `Sicurezza:RecuperoPassword:*`): codice valido 10 minuti, 5 tentativi per richiesta (poi la richiesta è annullata anche se si indovina), link valido 30 minuti, 3 richieste all'ora per account (tenant + email, esista o no) e 10 all'ora per indirizzo IP; in più il limite di richieste al minuto per IP delle pagine di accesso.
- **Fase 2 – codice:** il browser è legato alla richiesta da un cookie `.DvCms.Recupero` (riferimento casuale a 256 bit; sul server c'è solo l'hash), HttpOnly, SameSite Strict, `Path=/admin/recupero`, 30 minuti, Secure fuori da sviluppo. Con il codice giusto parte la **seconda email**, distinta, con il link monouso; il link non viene mai mostrato a schermo. Per le richieste `Ignorata` il codice non può mai essere giusto, e i messaggi sono identici.
- **Fase 3 – link:** il token è 256 bit casuali, salvato solo come hash SHA-256; la pagina risponde `Referrer-Policy: no-referrer` e `Cache-Control: no-store` e la modifica avviene con una POST con antiforgery. Un link scaduto, già usato, inventato, di un altro tenant o di un account nel frattempo disabilitato dà sempre lo stesso errore generico. Una password non valida o una conferma diversa non consumano il link.
- **Effetti della reimpostazione:** nuova password (BCrypt, politica 12–128 caratteri), `TimbroSicurezza` rinnovato (le sessioni esistenti decadono), blocco per tentativi falliti azzerato, link e tutte le altre richieste aperte dell'account invalidati, **notifica email** del cambio, voce di audit `RecuperoPasswordCompletato` senza segreti. Una nuova richiesta invalida codice e link delle precedenti. L'uso concorrente dello stesso link riesce una sola volta.
- **Segreti:** nel database ci sono solo gli hash di codice (con sale), riferimento e token. Le due email di recupero sono messaggi outbox *riservati*: anche il loro contenuto (codice, link) è cifrato nella tabella `MessaggiOutbox`. L'invio avviene nel worker; un tenant senza email configurata non invia e il problema compare nella schermata email del tenant, mentre l'utente vede la risposta di sempre.
- **Limiti noti:** i tempi di risposta di richieste reali e non reali non sono identici al microsecondo (le reali accodano un'email cifrata); il lavoro sul database è però lo stesso e l'invio non è sincrono. Il codice a 6 cifre è salvato con un hash semplice con sale: protegge dalla lettura casuale, non da un attacco offline, ma vale solo 10 minuti. Le righe scadute vengono ripulite dalla manutenzione (`operations.md`, G-001): richieste di recupero dopo 30 giorni (i limiti orari ne usano solo l'ultima ora).

## Inviti dei TenantAdmin (M1b)

- **Creazione:** nel dettaglio del tenant il `PlatformAdmin` può creare il `TenantAdmin` con "Invia un invito via email" (disponibile solo se il tenant ha il canale email configurato; altrimenti resta la password temporanea di M1). L'account nasce con una password casuale che nessuno conosce e `DeveCambiarePassword`; l'invito (`InvitiAccount`) conserva solo l'hash SHA-256 del token (256 bit casuali) e la scadenza (72 ore, `Sicurezza:Inviti:OreValidita`).
- **Email:** accodata nell'outbox nella stessa `SaveChanges` della creazione (riservata, quindi cifrata anche nell'outbox), con il link `…/admin/invito?token=…` costruito sul **dominio verificato e attivo del tenant invitato** (non su quello da cui lavora il `PlatformAdmin`), con lo schema della richiesta.
- **Accettazione:** `/admin/invito` è raggiungibile senza sessione ma con il limite di richieste degli accessi, `Referrer-Policy: no-referrer` e `no-store`; la scelta avviene con una POST con antiforgery e la stessa politica password del resto. Il link vale solo sull'host del tenant dell'invitato, solo una volta, finché non scade, l'account è attivo e non ha ancora scelto una propria password. Risposte non distinguibili per link scaduto, già usato, inesistente o di un altro tenant. Alla riuscita: nuova password, `DeveCambiarePassword` spento, sessioni precedenti invalidate, inviti aperti dell'account annullati, azione `InvitoAccettato` nell'audit.
- **Reinvio:** solo il `PlatformAdmin`, solo per un `TenantAdmin` attivo che non ha ancora scelto la password; i link precedenti si annullano. Dopo, la via è il recupero password.
- **Errori:** canale email non configurato → nessun account creato; chiave dei segreti mancante → nessun account creato. L'audit registra `CreazioneTenantAdmin` / `InvitoInviato` senza il link.

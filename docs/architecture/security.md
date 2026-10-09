# Sicurezza

La sicurezza è un requisito di progetto, con particolare attenzione a isolamento tenant, editor visuale, file caricati e operazioni di pagamento.

## Identità e autorizzazione

- Usare ASP.NET Core Identity e cookie sicuri per l'admin. Nessuna registrazione self-service per account amministrativi; la registrazione degli acquirenti esiste solo nei siti con e-commerce (ADR-0011, ADR-0014).
- Il `TenantAdmin` accede solo al proprio tenant; il `PlatformAdmin` a tutti, con i poteri del `TenantAdmin` più il menu Platform.
- Autorizzare ogni operazione in base a identità, tenant dell'account, sito, ruolo e risorsa.
- Ruoli: `PlatformAdmin` (globale), `TenantAdmin` (un solo tenant, più `TenantAdmin` per tenant), `Utente` (acquirente di un tenant, solo ordini), `Guest` (anonimo, sempre vincolato a un tenant).
- Solo il `PlatformAdmin` crea o eleva `TenantAdmin`; nessun ruolo assegna un ruolo superiore al proprio né modifica il proprio. La registrazione crea solo `Utente`; il ruolo non è mai preso dalla richiesta. Controllo lato server con test di non-escalation.
- Il primo `PlatformAdmin` si crea con un comando di bootstrap, senza credenziali predefinite.
- Ingresso del `PlatformAdmin` nell'admin di un tenant e azioni del menu "Platform" sono registrati in `AuditLog`.
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

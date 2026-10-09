# Sicurezza

La sicurezza è un requisito di progetto, con particolare attenzione a isolamento tenant, editor visuale, file caricati e operazioni di pagamento.

## Identità e autorizzazione

- Usare ASP.NET Core Identity e cookie sicuri per l'admin. Nessuna registrazione self-service: gli account sono creati da chi ha il ruolo per farlo (ADR-0011, ADR-0014).
- Autorizzare ogni operazione in base a identità, membership tenant/site, ruolo e risorsa.
- Ruoli: `PlatformAdmin` (globale), `TenantAdmin` e `Utente` (per membership), `Guest` (non autenticato). I permessi dell'`Utente` (`Content.Edit`, `Content.Publish`, `Media.Manage`, `Store.Manage`) sono assegnati dal `TenantAdmin`.
- Solo il `PlatformAdmin` crea o eleva `TenantAdmin`; nessun ruolo assegna un ruolo superiore al proprio né modifica il proprio. Controllo lato server con test di non-escalation.
- Il primo `PlatformAdmin` si crea con un comando di bootstrap, senza credenziali predefinite.
- Le operazioni dell'area di piattaforma sono registrate in `AuditLog`.
- Separare privilegi globali da ruoli tenant.
- Proteggere form mutanti con antiforgery; applicare policy session/cookie appropriate.
- Evitare IDOR: conoscere un GUID non equivale ad avere accesso.

## Dati tenant-scoped

- Tenant context derivato da host/account membership, non dal payload.
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

- HTTPS obbligatorio in produzione.
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

## Privacy e audit

Minimizzare dati personali, definire retention ed export/delete secondo requisiti applicabili. L'implementazione finale di privacy, consensi, DPA e policy di conservazione richiede una verifica legale del mercato target; l'agente non deve inventare conformità.

## Security gate prima del go-live

- Threat model aggiornato.
- Test isolamento tenant per CRUD/API/media/cache/jobs/commerce.
- Test XSS, CSRF, IDOR, upload e webhook.
- Segreti gestiti fuori dal repository.
- Backup/restore testati.
- Dipendenze con vulnerabilità note valutate.

# Revisione di sicurezza (G-005)

Revisione dello stato dell'applicazione alla fine di M3 (gestione tenant, pagine, temi, media, email, coda, cache), contro la checklist "Security gate prima del go-live" di `docs/architecture/security.md`. Non sostituisce un penetration test esterno prima del go-live: dice cosa è stato controllato, cosa è stato corretto e cosa resta aperto.

## Metodo

1. **Lettura del codice** contro la checklist e i principi di `security.md` e `multi-tenancy.md`.
2. **Revisione indipendente**: una seconda revisione fatta da un revisore separato, che ha ricevuto solo il codice e il modello di sicurezza dichiarato, senza le mie conclusioni. Ha cercato vulnerabilità sfruttabili in isolamento tra tenant, autorizzazione, accesso e recupero, upload, segreti, email, intestazioni, SQL.
3. **Prove**: la CSP è stata verificata con Chromium reale (tutte le schermate dell'admin e del sito pubblico, nei due temi, con dropdown e cambio provider email): nessuna violazione. Scansione delle dipendenze (`dotnet list package --vulnerable --include-transitive`): nessuna vulnerabilità nota. Ricerca di segreti nel repository: nessuno (solo chiavi finte nei test).
4. **Test che restano**: ogni problema corretto ha un test di regressione; due controlli strutturali (`MatriceSicurezzaPagineTest`, `IntestazioniSicurezzaWebTest`) coprono anche le pagine future.

## Esito della checklist

| Voce del gate | Stato | Dove si verifica |
|---|---|---|
| Threat model aggiornato | Fatto (sezione sotto) | Questo documento |
| Isolamento tenant per CRUD, media, cache, job | Verificato | `TenantIsolationTests` (pagine, account, domini, piattaforma, media, cache), `MediaWebTest`, `CacheWebTest`; nessun uso di `IgnoreQueryFilters` senza vincolo di tenant o riverifica del `PlatformAdmin` (revisione indipendente) |
| Isolamento per API e commerce | Non applicabile ancora | Commerce (M7) non esiste: da ripetere con quell'area |
| XSS | Verificato | Nessun `Html.Raw` in `src/`; contenuto in testo semplice codificato; URL dei pulsanti `/x` o `https://` ricontrollati in uscita; temi con colori `#RRGGBB`; CSP senza script in linea; test sulle pagine renderizzate (nessun `<script>` né `on*=` in linea) |
| CSRF | Verificato | Antiforgery su tutti i POST (cookie `SameSite=Strict`), `MatriceSicurezzaPagineTest` controlla ogni pagina con un gestore POST, anche future; handler GET senza effetti |
| IDOR | Verificato | Id di altri tenant o siti → 404 / `NonTrovato` in pagine, revisioni, redirect, media, temi, coda; test per ognuno |
| Upload | Verificato | `RilevatoreImmagini`, `ServizioMedia`, `ArchivioFileSystem`: firma, estensione, dimensioni, nome, chiave, path traversal, download con `nosniff` e CSP `sandbox` |
| Webhook | Non applicabile ancora | Pagamenti (M7) |
| Segreti fuori dal repository | Verificato | Ricerca nel repository; chiave dei segreti e credenziali solo da ambiente; segreti cifrati nel database |
| Backup/restore testati | Fatto | `docs/operations/backup-e-ripristino.md`, `RipristinoTest` |
| Dipendenze con vulnerabilità note | Verificato e automatizzato | Passo "Verifica vulnerabilità delle dipendenze" in CI (fallisce se ce n'è una) e Dependabot settimanale |

## Cosa è stato aggiunto in questa revisione

- **Intestazioni su ogni risposta** (`MiddlewareIntestazioniSicurezza`): `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (fotocamera, microfono, geolocalizzazione, USB negati), `Cross-Origin-Opener-Policy: same-origin`, **CSP** (`default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'`, `frame-ancestors 'self'` per il sito e `'none'` per l'admin) e per l'admin anche `X-Frame-Options: DENY` e `Cache-Control: no-store`. HSTS (180 giorni, senza sottodomini né precaricamento) fuori da Development. `Sicurezza:Csp:SoloSegnalazione=true` passa la CSP in modalità solo-segnalazione (utile quando arriverà il page builder).
- **Chiavi dei cookie e dell'antiforgery**: nome applicazione fisso e, con `Sicurezza:ChiaviCookie:Percorso`, anello di chiavi su una cartella persistente e condivisa. Senza, il valore di default (profilo dell'utente del processo) perde le sessioni a ogni contenitore ricreato e non funziona con più istanze: l'avvio lo segnala con un avviso. La cartella contiene chiavi in chiaro: permessi riservati all'utente del processo e al volume.
- **Avviso all'avvio** se `ProxyAttendibili` non è configurato fuori da Development.
- **Dipendenze**: controllo in CI e Dependabot.

## Problemi trovati e corretti

| # | Gravità | Problema | Correzione | Test |
|---|---|---|---|---|
| 1 | Media | Il contatore dei tentativi di accesso falliti si salvava in modo ottimistico e ignorava i conflitti: con molti tentativi in parallelo sullo stesso account se ne contava uno solo, rimandando il blocco e permettendo di provare molte password | Incremento atomico sul database (`Contatore = Contatore + 1`) e blocco con un secondo aggiornamento condizionato alla soglia | `TentativiParalleliSbagliatiVengonoTuttiContatiEBloccanoLAccount` (fallisce con il codice precedente) |
| 2 | Media | Il limite di richieste per indirizzo usava l'IPv6 completo: chi ha un /64 ruotava gli indirizzi e non aveva limite | Chiave per IPv6 = rete /64 (`ChiaveIndirizzoIp`), per accesso, recupero e inviti | `ChiaveIndirizzoIpTest` |
| 3 | Bassa | La chiave di cache usava lo slug grezzo: `/%20about`, `/About`, … erano voci distinte della stessa pagina e potevano riempire la cache espellendo quelle degli altri tenant | Chiave dallo slug normalizzato; uno slug che non si normalizza non va in cache | `VariantiEquivalentiDiUnoSlugCondividonoUnaSolaVoceDiCache` |
| 5 | Bassa | L'email di un `PlatformAdmin` che modificava una pagina compariva come autore della revisione ai `TenantAdmin` | L'autore di piattaforma si mostra come "Amministratore della piattaforma" | `LAutoreDiUnaRevisioneFattaDalPlatformAdminNonMostraLaSuaEmailAlTenant` |
| 6 | Bassa/operativa | Con `ProxyAttendibili` vuoto dietro un reverse proxy i limiti per indirizzo diventano globali, lo schema resta `http` e non c'è HSTS, senza alcun avviso | Avviso all'avvio fuori da Development; documentato in `operations.md` | — |

## Rischi accettati (da confermare)

Questi punti non sono stati corretti. Restano finché il committente non decide altrimenti.

| Rischio | Perché si accetta | Mitigazione attuale |
|---|---|---|
| **Blocco dell'account usabile per negare l'accesso** (#4 della revisione): 5 tentativi sbagliati bloccano un account per 5 minuti, anche il `PlatformAdmin` (l'email del `PlatformAdmin` è accettata da ogni host); le richieste di recupero sono limitate per account | Contare per coppia (account, indirizzo) indebolirebbe il blocco contro attacchi distribuiti; un blocco breve è il compromesso classico. Il `PlatformAdmin` non ha recupero password, quindi un blocco ripetuto va sbloccato a mano | Blocco di soli 5 minuti, limite di richieste per indirizzo, audit dei blocchi nei log. Con la 2FA attiva il secondo fattore consuma lo stesso contatore dei tentativi (ADR-0019); la password corretta non lo azzera finché il codice non è verificato |
| **Token nell'indirizzo** dei link di recupero e di invito (`?token=…`) | Inevitabile per un link da email; i token sono monouso, scadono (30 min, 72 ore), sono 256 bit casuali e si salva solo l'hash | `Referrer-Policy: no-referrer` e `no-store` sulle pagine che li ricevono; la CSP non permette risorse esterne. I log di accesso del proxy possono registrare l'indirizzo: tenerli con accesso ristretto |
| **`style-src 'unsafe-inline'`** nella CSP | I temi usano l'attributo `style` per le variabili dei colori; oggi nessun utente può iniettare CSS (colori validati `#RRGGBB`, testi codificati) | Gli script restano vietati in linea; da rivedere con il page builder |
| **Nessuna scansione antimalware** dei file caricati | Sono ammesse solo immagini, riconosciute dalla firma, servite con `sandbox` e `nosniff`; la scansione dipende dall'infrastruttura | Campo `StatoScansione` e quarantena già modellati; da decidere prima di aprire altri formati |
| ~~Nessuna 2FA~~ | Risolto con G-006 (ADR-0019): TOTP con codici di recupero, obbligatoria per `PlatformAdmin` e `TenantAdmin` per impostazione predefinita | — |
| **Quote per tenant** su file e richieste | Non richieste per M3 | Limite per file e per immagine; limite di richieste solo su accesso, recupero e inviti |
| **Host header nei link di recupero** costruiti da `Request.Host` | Kestrel rifiuta gli host malformati (provato con `legit.com:80@evil.com`, `:abc`, ecc.); resta valida solo una porta numerica, non sfruttabile | Hardening possibile: usare l'host del tenant, come fanno gli inviti |

## Threat model (sintesi)

| Minaccia | Dove | Controlli |
|---|---|---|
| Un tenant legge o modifica dati di un altro | Query, servizi, media, cache | Tenant dal solo host; filtri globali; chiavi esterne composte; servizi che riverificano l'attore; chiavi di cache e archivio per sito; test d'isolamento per ogni area |
| Escalation di ruolo | Admin, area Platform | Solo il `PlatformAdmin` crea o eleva `TenantAdmin`; politiche e riverifica sul database; audit |
| Furto di sessione o di credenziali | Accesso, recupero, inviti | BCrypt, blocco, limiti, risposte uniformi, token a 256 bit con solo l'hash, cookie `HttpOnly`/`Secure`/Path `/admin`, timbro di sicurezza, intestazioni di cache |
| Contenuto malevolo (XSS) | Pagine, temi, nomi file, email | Solo testo semplice codificato; CSP; allow-list di URL, colori e tipi di campo |
| Upload malevolo | Media | Firma + estensione + dimensioni; chiavi del server; `sandbox`; nessun SVG/HTML |
| Furto dei segreti dei tenant | Database, backup | Cifratura AES-GCM con chiave maestra fuori dal database; sola scrittura nella UI; backup cifrati e chiave separata |
| Abuso e negazione del servizio | Accesso, upload, cache | Limiti di richieste (IPv6 per /64), limiti di dimensione e di pixel, cache a voci limitate, chiave di cache normalizzata |
| Compromissione dell'infrastruttura (proxy, chiavi dei cookie, chiave dei segreti) | Deploy | Proxy attendibili esplicitati; chiavi dei cookie su volume dedicato; chiave dei segreti separata dal backup; avvisi all'avvio |
| Dipendenze vulnerabili | Build | Controllo in CI e Dependabot |

## Da rifare prima del go-live

- Penetration test esterno (almeno accesso, recupero, upload, isolamento tenant).
- Ripetere questa revisione con le aree che arriveranno (page builder, e-commerce e pagamenti, registrazione degli acquirenti, webhook).
- Verificare in produzione: HTTPS e HSTS dietro il proxy reale, `ProxyAttendibili`, `Sicurezza:ChiaviCookie:Percorso`, permessi della cartella dell'archivio, CSP senza violazioni nei log del browser (si può usare `Sicurezza:Csp:SoloSegnalazione`).

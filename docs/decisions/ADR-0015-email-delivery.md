# ADR-0015 — Invio email tramite adapter intercambiabili

- **Stato:** Accepted (integrato da [ADR-0021](ADR-0021-email-oauth-per-tenant.md): i canali con consenso OAuth si configurano dal `TenantAdmin`)
- **Data:** 2026-10-09

## Contesto

Il progetto deve inviare email: recupero password (OTP e link monouso, ADR-0011), conferma registrazione degli acquirenti ed email transazionali dell'e-commerce (ADR-0009). Il committente richiede il supporto di quattro canali di invio: Microsoft Graph API, Gmail API, SendGrid e SMTP.

## Decisione

- L'applicazione dipende da un contratto unico (ad esempio `IInvioEmail`, definito in `DvCms.Application`) e non conosce il canale di invio.
- Sono previsti quattro adapter in `DvCms.Infrastructure`: **Microsoft Graph API**, **Gmail API**, **SendGrid**, **SMTP**.
- **Il canale è una proprietà del tenant.** Il tenant ha un campo `ProviderEmail` (`Smtp`, `Graph`, `Gmail`, `SendGrid`) e le impostazioni del canale scelto (`ImpostazioniEmailTenant`). Non esiste un ordine di priorità fra i canali: ogni tenant usa quello configurato.
- La configurazione si modifica solo dalla schermata di configurazione del tenant nel menu "Platform" (solo `PlatformAdmin`). La UI mostra un selettore del tipo di provider e, in base alla scelta, **solo i campi necessari** a quel provider. Campi indicativi (da affinare in implementazione):
  - SMTP: host, porta, modalità TLS, utente, password, indirizzo e nome mittente.
  - Microsoft Graph: tenant Microsoft, client id, client secret, casella mittente.
  - Gmail API: credenziali OAuth o account di servizio, utente mittente.
  - SendGrid: API key, indirizzo e nome mittente.
- Validazione lato server dei campi in base al provider scelto; un provider non riconosciuto o impostazioni incomplete sono rifiutati al salvataggio. La schermata offre un invio di prova.
- **Segreti nel database:** password, API key e client secret dei tenant sono cifrati a riposo con ASP.NET Core Data Protection, sono in sola scrittura nella UI (mai mostrati di nuovo, solo sostituibili) e non compaiono mai nei log. Dove stanno le chiavi: vedi "Chiavi di cifratura".
- L'invio avviene nel worker: il messaggio outbox contiene il `IdTenant`, e il worker carica le impostazioni di quel tenant. Un tenant senza configurazione valida non invia; l'errore è visibile ai `PlatformAdmin`.
- Tutti i messaggi hanno un tenant (quello dell'host): il `PlatformAdmin` non ha recupero password, quindi non esistono invii senza tenant.
- Le email partono dall'outbox (ADR-0010): l'invio avviene nel worker, con retry e idempotenza. Mai nella richiesta web.
- Le due email del recupero password (codice OTP e link monouso) sono messaggi distinti, inviati in momenti diversi.
- I template email sono parte del codice (server-side), non modificabili dai tenant. Nessun segreto, OTP o token in chiaro nei log.
- Ogni adapter ha test con un fake del canale esterno e un test di contratto comune. Non si inviano email reali dalla CI.

## Chiavi di cifratura

- La chiave che protegge i segreti sta nella **configurazione dell'ambiente** (`appsettings.{Environment}.json` non versionato, oppure variabile d'ambiente), mai nel repository.
- Il key ring di Data Protection può essere persistito nel database, purché sia protetto da quella chiave o da un certificato tenuto fuori dal database. Chiavi in chiaro nello stesso database dei segreti non proteggono da una copia del database.
- Il backup della chiave è separato dal backup del database e deve essere ripristinabile: senza la chiave, i segreti dei tenant non sono più leggibili e vanno reinseriti.

## Ordine di implementazione

Non c'è una priorità fra i canali per l'utilizzo. Per lo sviluppo si parte da SMTP (comodo con un catcher locale) e si aggiungono Graph, Gmail e SendGrid; contratto, selezione per tenant e UI dinamica vanno pronti dall'inizio, per non legare il codice al primo canale.

## Alternative considerate

- Un solo provider: non soddisfa il requisito.
- Configurazione globale per installazione: respinta, ogni tenant può usare un canale diverso.
- Invio sincrono dalla richiesta: perde messaggi in caso di errore.

## Conseguenze

- Ogni SDK o libreria aggiunta (Microsoft Graph, Google API, SendGrid, libreria SMTP) rispetta la regola 11 di `AGENTS.md`: motivazione, licenza, manutenzione, limitata a `DvCms.Infrastructure`. In alternativa si valuta `HttpClient` diretto.
- Ogni canale richiede configurazione esterna manuale (registrazione app in Microsoft Entra, progetto/OAuth Google, account SendGrid, server SMTP) e dominio mittente con SPF/DKIM/DMARC; queste attività sono parte del runbook (`docs/architecture/operations.md`).
- Ogni tenant ha le proprie credenziali e il proprio mittente: il `PlatformAdmin` deve configurarle per ogni tenant, con SPF/DKIM/DMARC del dominio usato.
- Servono la cifratura dei segreti nel database e un processo per conservare e ripristinare la chiave (sezione "Chiavi di cifratura").

## Attuazione (A-005)

- **SMTP con MailKit** (`MailKit` 4.18, licenza MIT, mantenuta; porta con sé `MimeKit` e `BouncyCastle.Cryptography`, senza vulnerabilità note al momento dell'aggiunta). `System.Net.Mail.SmtpClient` è sconsigliato da Microsoft e non supporta il TLS immediato (porta 465). Il pacchetto è usato solo in `DvCms.Infrastructure`. Modalità di sicurezza della connessione: `Nessuna`, `StartTls`, `TlsImmediato`; i certificati del server si validano sempre.
- **Contratti in `DvCms.Application`:** `IInvioEmail` (invio per tenant, usato solo dal worker), `IAdapterEmail` (un canale), `IAccodatoreEmail` (accodamento nell'outbox, nella transazione del chiamante; con `riservato` il contenuto è cifrato anche nell'outbox, per OTP e link). I campi e le regole dei quattro canali stanno in `DefinizioniProviderEmail`, unica fonte per UI, validazione e adapter. Gli altri tre canali sono descritti sotto (A-006, A-007, A-008).
- **Segreti:** `ProtettoreSegreti` usa Data Protection in un'istanza dedicata (separata da cookie e antiforgery). Il key ring sta nella tabella `ChiaviSegrete`, cifrato con AES-GCM dalla chiave maestra di ambiente `Sicurezza:ChiaveSegreti` (32 byte in base64, `openssl rand -base64 32`; user-secrets o variabile `Sicurezza__ChiaveSegreti`). Senza chiave i segreti non si cifrano né si leggono e l'errore lo dice, senza mostrare valori. I segreti delle impostazioni (`SegretiProtetti`) non compaiono in `ValoriJson`, nei log né negli errori.

## Attuazione (A-006, A-007, A-008)

- **Nessun SDK: `HttpClient` diretto** (alternativa prevista da questo ADR). Microsoft Graph, Gmail API e SendGrid hanno ciascuno un'API REST di poche richieste e gli SDK ufficiali (Microsoft.Graph, Google.Apis.Gmail, SendGrid) porterebbero con sé molte dipendenze transitive e superficie di aggiornamento per una sola chiamata. Si aggiunge soltanto `Microsoft.Extensions.Http` 10.0.12 (MIT, Microsoft, stessa famiglia degli altri pacchetti `Microsoft.Extensions.*` già usati) per `IHttpClientFactory` (durata dei gestori e DNS), solo in `DvCms.Infrastructure`.
- **Sicurezza comune:** endpoint fissi nel codice (nessun indirizzo preso dalle impostazioni del tenant: niente SSRF); attesa massima di 30 secondi per richiesta; intestazioni con a capo rifiutate (oggetto, destinatario, nome); gli errori riportano canale e codice HTTP ma mai il corpo della risposta né valori delle impostazioni (`InvioEmailFallitoException`); i token OAuth non si salvano né si mettono in cache (uno per invio, nessuna contaminazione fra tenant). Un errore dell'adapter fa ritentare il messaggio dall'outbox come per SMTP.
- **SendGrid** (A-008): `POST https://api.sendgrid.com/v3/mail/send` con la chiave API come Bearer; contenuti `text/plain` e, se presente, `text/html`. Il mittente deve essere un indirizzo o un dominio verificato nell'account SendGrid.
- **Microsoft Graph** (A-006): flusso *client credentials* (`login.microsoftonline.com/{tenant}/oauth2/v2.0/token`, scope `https://graph.microsoft.com/.default`) e `POST /v1.0/users/{casella}/sendMail` (`saveToSentItems` falso). Il tenant e l'identificativo client sono validati come identificativi (lettere, cifre, `.`, `_`, `-`) perché finiscono nell'indirizzo; la casella è codificata come segmento. Invia HTML se presente, altrimenti testo (Graph non invia le due versioni insieme).
- **Gmail API** (A-007): account di servizio con delega a livello di dominio. Il JSON delle credenziali (segreto, validato: `type` = `service_account`, `client_email`, `private_key`) serve a firmare un JWT RS256 (`sub` = utente mittente, scope `gmail.send`) che si scambia con un token su `oauth2.googleapis.com/token` (il `token_uri` del JSON si ignora); il messaggio MIME è inviato con `users/me/messages/send` (`raw` in base64url).
- **Test:** servizio HTTP finto, nessuna richiesta esterna. Un test di contratto comune ai tre adapter (Bearer, destinatario, intestazioni con a capo, errori senza corpo né segreti, servizio non raggiungibile) più verifiche specifiche: richiesta del token e codifica della casella (Graph), firma del JWT verificata con la chiave pubblica, rivendicazioni e MIME decodificato (Gmail), forma del JSON ed elisione dei campi vuoti (SendGrid).


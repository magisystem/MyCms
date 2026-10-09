# ADR-0015 — Invio email tramite adapter intercambiabili

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Il progetto deve inviare email: recupero password (OTP e link monouso, ADR-0011), conferma registrazione degli acquirenti ed email transazionali dell'e-commerce (ADR-0009). Il committente richiede il supporto di quattro canali di invio: Microsoft Graph API, Gmail API, SendGrid e SMTP.

## Decisione

- L'applicazione dipende da un contratto unico (ad esempio `IEmailSender`, definito in `DvCms.Application`) e non conosce il canale di invio.
- Sono previsti quattro adapter in `DvCms.Infrastructure`: **Microsoft Graph API**, **Gmail API**, **SendGrid**, **SMTP**.
- **Il canale è una proprietà del tenant.** Il tenant ha un campo `EmailProvider` (`Smtp`, `Graph`, `Gmail`, `SendGrid`) e le impostazioni del canale scelto (`TenantEmailSettings`). Non esiste un ordine di priorità fra i canali: ogni tenant usa quello configurato.
- La configurazione si modifica solo dalla schermata di configurazione del tenant nel menu "Platform" (solo `PlatformAdmin`). La UI mostra un selettore del tipo di provider e, in base alla scelta, **solo i campi necessari** a quel provider. Campi indicativi (da affinare in implementazione):
  - SMTP: host, porta, modalità TLS, utente, password, indirizzo e nome mittente.
  - Microsoft Graph: tenant Microsoft, client id, client secret, casella mittente.
  - Gmail API: credenziali OAuth o account di servizio, utente mittente.
  - SendGrid: API key, indirizzo e nome mittente.
- Validazione lato server dei campi in base al provider scelto; un provider non riconosciuto o impostazioni incomplete sono rifiutati al salvataggio. La schermata offre un invio di prova.
- **Segreti nel database:** password, API key e client secret dei tenant sono cifrati a riposo con ASP.NET Core Data Protection, sono in sola scrittura nella UI (mai mostrati di nuovo, solo sostituibili) e non compaiono mai nei log. Dove stanno le chiavi: vedi "Chiavi di cifratura".
- L'invio avviene nel worker: il messaggio outbox contiene il `TenantId`, e il worker carica le impostazioni di quel tenant. Un tenant senza configurazione valida non invia; l'errore è visibile ai `PlatformAdmin`.
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

# ADR-0015 — Invio email tramite adapter intercambiabili

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Il progetto deve inviare email: recupero password (OTP e link monouso, ADR-0011), conferma registrazione degli acquirenti ed email transazionali dell'e-commerce (ADR-0009). Il committente richiede il supporto di quattro canali di invio: Microsoft Graph API, Gmail API, SendGrid e SMTP.

## Decisione

- L'applicazione dipende da un contratto unico (ad esempio `IEmailSender`, definito in `DvCms.Application`) e non conosce il canale di invio.
- Sono previsti quattro adapter in `DvCms.Infrastructure`: **Microsoft Graph API**, **Gmail API**, **SendGrid**, **SMTP**.
- Il canale attivo si sceglie con una chiave di configurazione esplicita (ad esempio `Email:Provider` = `Smtp`, `Graph`, `Gmail`, `SendGrid`). Un valore non riconosciuto fa fallire l'avvio, senza fallback silenzioso.
- Credenziali e segreti (client secret, API key, password SMTP, credenziali OAuth) restano fuori dal repository: user-secrets in sviluppo, environment variables o secret manager in produzione.
- Le email partono dall'outbox (ADR-0010): l'invio avviene nel worker, con retry e idempotenza. Mai nella richiesta web.
- Le due email del recupero password (codice OTP e link monouso) sono messaggi distinti, inviati in momenti diversi.
- I template email sono parte del codice (server-side), non modificabili dai tenant. Nessun segreto, OTP o token in chiaro nei log.
- Ogni adapter ha test con un fake del canale esterno e un test di contratto comune. Non si inviano email reali dalla CI.

## Ordine di implementazione

Proposta: **SMTP** per primo (sviluppo con un catcher locale), poi gli altri tre in ordine da stabilire. Il contratto e la selezione da configurazione vanno però pronti dall'inizio, per evitare di legare il codice al primo canale.

## Alternative considerate

- Un solo provider: non soddisfa il requisito.
- Invio sincrono dalla richiesta: perde messaggi in caso di errore.

## Conseguenze

- Ogni SDK o libreria aggiunta (Microsoft Graph, Google API, SendGrid, libreria SMTP) rispetta la regola 11 di `AGENTS.md`: motivazione, licenza, manutenzione, limitata a `DvCms.Infrastructure`. In alternativa si valuta `HttpClient` diretto.
- Ogni canale richiede configurazione esterna manuale (registrazione app in Microsoft Entra, progetto/OAuth Google, account SendGrid, server SMTP) e dominio mittente con SPF/DKIM/DMARC; queste attività sono parte del runbook (`docs/architecture/operations.md`).
- Resta aperto se la configurazione dell'invio sia unica per tutta l'installazione o possa essere diversa per tenant (mittente per tenant).

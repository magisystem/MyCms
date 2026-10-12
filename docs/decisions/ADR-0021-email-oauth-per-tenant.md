# ADR-0021 — Email con consenso OAuth (Microsoft 365, Google Mail), app OAuth del tenant

- **Stato:** Accepted (decisione del committente, 2026-10-12)
- **Data:** 2026-10-12
- **Modifica:** ADR-0015 (la configurazione di questi due canali non spetta al `PlatformAdmin`)

## Contesto

ADR-0015 prevede Microsoft Graph (credenziali applicative, permesso `Mail.Send` senza utente) e Gmail API (account di servizio con delega a livello di dominio). Entrambi funzionano solo con un amministratore del dominio che concede deleghe ampie: l'app Graph, senza una *Application Access Policy*, può inviare da qualunque casella del tenant Microsoft; la delega Google esiste solo in Google Workspace e le caselle `@gmail.com` non la supportano. Il committente chiede che i tenant possano inviare dalla **propria casella Microsoft 365 o Google Mail con le loro API** e che l'app OAuth sia **del tenant**, non della piattaforma: un'app unica per tutte le installazioni sarebbe un punto di compromissione comune, e il suo segreto non può stare in mano a chi amministra la piattaforma.

## Decisione

- Due nuovi canali, **`MicrosoftOAuth`** e **`GoogleOAuth`**, con consenso dell'utente (OAuth 2.0, *authorization code* con **PKCE** S256). I canali di ADR-0015 restano invariati.
- **L'app OAuth è del tenant.** Il suo `TenantAdmin` la registra presso Microsoft Entra o Google Cloud e inserisce nella pagina **«Email»** dell'amministrazione (`/admin/email`) tenant Microsoft (solo Microsoft), identificativo client e segreto del client. **Solo il `TenantAdmin` del tenant** legge e modifica questi dati, la pagina e il servizio lo riverificano sul database a ogni operazione (cartella con la politica `SoloAmministratoreTenant`, quindi 403 anche al `PlatformAdmin`). Il segreto del client è in sola scrittura e cifrato a riposo come gli altri segreti (ADR-0015).
- **Il `PlatformAdmin` non vede né modifica l'app OAuth.** Nella sua pagina del tenant vede solo il nome del canale e può accodare l'email di prova; non può salvare un canale OAuth né sovrascrivere o togliere uno esistente (`NonConsentito`, registrato nell'audit). Il `TenantAdmin` può rimuovere il canale; da lì la piattaforma può configurarne un altro. Il `TenantAdmin` che salva un'app OAuth su un tenant con un canale della piattaforma lo **sostituisce** solo con una conferma esplicita.
- **Collegamento della casella.** Il pulsante «Collega la casella» avvia il consenso: `state` casuale e verificatore PKCE sono messi, cifrati con la protezione dei segreti insieme a tenant, attore, canale, impronta dell'app, indirizzo di ritorno e scadenza (10 minuti), in un cookie `HttpOnly` limitato a `/admin/email`, `SameSite=Lax` (il ritorno dal provider è una navigazione fra siti). Il browser raggiunge il provider con una navigazione GET (`?handler=Vai`) e non con un reindirizzamento dopo l'invio di un modulo, perché la CSP `form-action 'self'` lo bloccherebbe in alcuni browser. Al ritorno (`/admin/email/collegamento`) il servizio controlla `state` in tempo costante, cookie, scadenza, tenant, attore, canale, impronta dell'app e indirizzo di ritorno prima di scambiare il codice con il segreto del client e il verificatore; rifiuta un consenso senza il permesso di invio e un provider che non emette un refresh token; legge l'indirizzo della casella (Microsoft: `/me`; Google: `userinfo`), che diventa il mittente.
- **Ambiti minimi.** Microsoft: `offline_access Mail.Send User.Read`; Google: `openid email gmail.send`. Nessun permesso di lettura della posta. Invio con `POST /me/sendMail` (Graph) e `users/me/messages/send` (Gmail), con endpoint fissi nel codice.
- **Token.** Il refresh token e il token di accesso stanno solo cifrati (`ConnessioniEmailOAuth`, una riga per tenant). L'invio (nel worker) riusa il token di accesso finché valido (margine di 2 minuti), altrimenti lo rinnova e salva il nuovo (Microsoft ruota il refresh token a ogni rinnovo; il rinnovo concorrente non è un errore). Se il provider risponde `invalid_grant` il collegamento passa a **Revocata**: gli invii successivi falliscono subito senza richiamare il provider, l'errore compare nella pagina del `TenantAdmin`, i tentativi dell'outbox restano limitati (ADR-0010). Cambiando canale, tenant Microsoft o identificativo client il collegamento si elimina (il consenso valeva per un'altra app); cambiando solo il segreto resta.
- **Scollegamento.** «Scollega» revoca il token presso Google (Microsoft non offre un endpoint per il singolo token: va rimosso l'accesso dall'account) ed elimina il collegamento; «Rimuovi il canale» elimina anche l'app.
- **Audit.** `ModificaAppEmailOAuth`, `CollegamentoEmailOAuth`, `ScollegamentoEmailOAuth`, con canale e indirizzo della casella; mai segreti, token, codici né corpi di risposta del provider (gli errori riportano canale, codice HTTP e il solo codice OAuth standard).
- **Nessuna nuova dipendenza:** `HttpClient` diretto, come in ADR-0015.

## Alternative considerate

- **App OAuth unica di piattaforma:** respinta dal committente (superficie d'attacco comune, segreto in mano al `PlatformAdmin`); Google richiederebbe inoltre la verifica dell'app per lo scope sensibile `gmail.send`.
- **Estendere i canali Graph/Gmail esistenti:** non risolve le caselle `@gmail.com` né l'ampiezza del permesso applicativo di Graph.
- **Token solo in memoria:** perderebbe il consenso a ogni riavvio.

## Conseguenze

- Ogni tenant registra la propria app: per Google, un'app in stato **«Testing»** fa scadere il refresh token dopo 7 giorni; per un uso stabile va pubblicata («In production») e, per lo scope `gmail.send`, può richiedere la verifica di Google oppure restare «non verificata» con l'avviso per gli utenti. Per Microsoft, i segreti del client hanno una scadenza (annotarla e rinnovarla). Vedi `docs/architecture/operations.md`.
- Il tenant resta responsabile del mittente (SPF, DKIM e DMARC del dominio della casella) e delle quote del provider (Gmail ~500 messaggi al giorno per gli account personali, limiti di Microsoft 365).
- Aperto: avviso prima della scadenza del segreto del client (richiede che il tenant ne indichi la data), prova con caselle reali di Microsoft 365 e Google (a cura del committente: le prove automatiche usano provider finti).

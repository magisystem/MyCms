# ADR-0026 — ALTCHA sulle form pubbliche

- **Stato:** Accepted (richiesta del committente del 2026-10-18: «integrare Altcha nelle form pubbliche»; ambito e attivazione scelti dal committente)
- **Data:** 2026-10-18
- **Modifica:** nessun ADR superato. Applica la protezione anti-abuso già prevista in [security.md](../architecture/security.md) alle form pubbliche.

## Contesto

Le form pubbliche del prodotto sono la **registrazione degli acquirenti** (`/account/registrazione`, ADR-0011) e la **richiesta di recupero password** (`/admin/recupero`, anche per gli acquirenti). Entrambe fanno partire un'email verso un indirizzo scelto da chi compila: senza un freno si prestano a invii automatici e a riempire le tabelle di account in attesa. Oggi hanno solo il limite di richieste per indirizzo IP. Non esiste ancora un modulo di contatto pubblico; quando ci sarà userà la stessa verifica.

Il committente ha scelto **ALTCHA**: prova di lavoro (proof-of-work) lato visitatore, senza servizi esterni, senza cookie di tracciamento, compatibile con il GDPR.

## Decisione

1. **Dove.** Registrazione degli acquirenti e richiesta di recupero password. Non l'accesso (ha già blocco dell'account e limiti), non le pagine successive del recupero (richiedono già un riferimento e un codice) e non le form dell'amministrazione autenticata.
2. **Server: `Altcha.AspNetCore` 1.1.0** (libreria ufficiale di ALTCHA, MIT; dipende da `Altcha` 1.1.0 e `BouncyCastle.Cryptography`, già presente per MailKit). Protocollo ALTCHA v2 con `PBKDF2/SHA-256`, sfida deterministica firmata con HMAC; la libreria controlla firma, scadenza (10 minuti) e soluzione, e impedisce il riuso (`IAltchaReplayStore` in memoria, per processo: va bene con una sola istanza; con più istanze serve un archivio condiviso, vedi sotto). Nessun codice di crittografia scritto in casa.
3. **Verifica nelle pagine.** `IVerificaAntiAbuso` (in `DvCms.Web/AntiAbuso`): `Attiva` dice se mostrare il widget, `VerificaAsync` controlla la prova del campo `altcha`. La pagina verifica **prima** di fare qualunque cosa con i dati; se fallisce mostra l'errore e non crea nulla (nessun account, nessuna email, nessun cookie di recupero). Nel registro finisce solo il codice dell'errore, mai la prova.
4. **Sfida:** `GET /altcha/sfida` (anonimo), `Cache-Control: no-store`, limite di richieste per indirizzo (`Altcha:RichiesteSfidaAlMinuto`, predefinito 30). Risponde 404 se la protezione è spenta.
5. **Widget: pacchetto npm `altcha` 3.4.0** (MIT), versione `external` copiata senza modifiche in `wwwroot/lib/altcha/` (elenco e licenza in `LICENZE-TERZE.txt`): niente CDN, niente worker da blob, nessuno script in linea, quindi **la CSP resta invariata** (`script-src 'self'`). I worker PBKDF2 si registrano da `wwwroot/js/altcha-avvio.js` (file della stessa origine). L'italiano è `i18n/it.js`; l'inglese è incluso. Il widget calcola la prova all'invio (`auto="onsubmit"`); senza JavaScript la form non si può inviare e un avviso lo dice.
6. **Chiave e attivazione globali** (scelta del committente): `Altcha:HmacSignatureSecret` unica per l'installazione, dai segreti o dall'ambiente, mai nel repository; la chiave che firma la soluzione è derivata da questa (`AltchaSecrets.DeriveHmacKeySecret`). `Altcha:Abilitato` (predefinito `true`) la accende o spegne per tutti. In `Development` senza chiave se ne genera una a ogni avvio; altrove la sua assenza non ferma l'avvio (come per le altre chiavi) ma scrive un errore e le form rifiutano ogni invio. Nessuna impostazione per sito.
7. **Difficoltà.** I predefiniti della libreria (5000 iterazioni, contatore fra 5000 e 10000) richiedevano circa 8 secondi in un Chromium senza testa con pochi core. I nostri predefiniti sono più leggeri: `Cost` 2500, contatore fra 2000 e 5000 (circa 1-2 secondi nella stessa prova, qualche secondo su un telefono). La protezione vera sono firma, scadenza, uso unico e limiti di richieste; il lavoro è un freno in più. Si cambia con `Altcha:Challenge:Cost`, `CounterMin`, `CounterMax`.

## Conseguenze

- Nuove dipendenze: `Altcha.AspNetCore` (NuGet) e il widget (file statici). Non si usa npm in compilazione: i file sono nel repository, come per Tabler.
- I test di integrazione tengono la protezione spenta (`Altcha:Abilitato=false` nella fabbrica) e la accendono solo nei test dedicati (`AltchaWebTest`), che risolvono le sfide con la stessa libreria; la prova nel browser vero (nessuna violazione della CSP, reindirizzamento dopo la verifica) è stata fatta a mano con Chromium.
- **Più istanze:** l'archivio dei riusi è per processo. Con più istanze dietro un bilanciatore un attaccante potrebbe riusare la stessa prova una volta per istanza; il danno è limitato dal limite di richieste e dal fatto che ogni form comunque verifica i propri dati. Se si passa a più istanze si registra `AddAltchaDistributedReplayStore()` o un archivio con inserimento atomico (documentazione della libreria).
- **Rotazione della chiave:** cambiare `Altcha:HmacSignatureSecret` invalida le sfide in corso (i visitatori che hanno la pagina aperta devono ricaricarla): è sicuro farlo in qualsiasi momento.
- **Un bot dedicato** può risolvere la prova di lavoro (costa tempo di calcolo, non è un test per esseri umani): ALTCHA alza il costo, non lo rende infinito. Il limite di richieste per indirizzo e la conferma via email restano le altre difese.

## Alternative scartate

- **reCAPTCHA / hCaptcha / Turnstile:** servizi esterni con tracciamento, contro la linea del prodotto (nessuna dipendenza esterna nelle pagine pubbliche).
- **Implementare il protocollo in casa:** esiste la libreria ufficiale, mantenuta e con replay protection; scrivere crittografia a mano non conviene.
- **Widget da CDN:** romperebbe la CSP `script-src 'self'` e ci farebbe dipendere da un terzo a ogni visita.
- **Attivazione per sito:** il committente ha scelto la chiave globale e l'attivazione per tutti; si può aggiungere in seguito senza cambiare il resto.

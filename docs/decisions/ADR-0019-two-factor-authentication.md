# ADR-0019 — Autenticazione a due fattori (TOTP e codici di recupero)

- **Stato:** Accepted
- **Data:** 2026-10-10
- **Riferimenti:** ADR-0011 (account e ruoli, 2FA rimandata), ADR-0016 (autenticazione locale), `docs/architecture/security.md`, backlog G-006

## Contesto

ADR-0011 prevedeva la 2FA come evoluzione, "non bloccante per il go-live". L'admin è esposto su Internet sull'host di ogni tenant e un account `TenantAdmin` o `PlatformAdmin` compromesso permette di pubblicare contenuti, leggere dati dei clienti e (per il `PlatformAdmin`) entrare in qualunque tenant. La sola password, anche con blocco dopo 5 tentativi e rate limiting, è un singolo punto di compromissione (riuso di password, phishing). Servono un secondo fattore, una via di recupero che non diventi la porta di servizio dell'attaccante e una regola chiara su chi è obbligato.

## Decisione

### Metodo: TOTP (RFC 6238)

- Codice a 6 cifre, HMAC-SHA1, passo di 30 secondi, finestra di tolleranza ±1 passo (deriva degli orologi), compatibile con le comuni app di autenticazione. Implementato con la sola libreria di base (HMAC e Base32 propri): nessuna dipendenza nuova da mantenere.
- Il segreto (20 byte casuali) è cifrato a riposo con `IProtettoreSegreti` (stessa chiave maestra `Sicurezza:ChiaveSegreti` degli altri segreti, ADR-0015). Non viene mai registrato nei log né restituito dopo l'attivazione.
- **Anti-replay:** si memorizza l'ultimo passo accettato (`UltimoPassoTotp`); un codice è valido solo se il suo passo è **maggiore**, con aggiornamento atomico sul database (`UPDATE ... WHERE UltimoPassoTotp < passo`). Lo stesso codice non è utilizzabile due volte, nemmeno con richieste concorrenti.
- Niente SMS ed email come secondo fattore: SIM-swap e dipendenza dalla casella di posta (che serve già al recupero password) vanificherebbero il secondo fattore. Passkey/WebAuthn è un'evoluzione possibile, non necessaria ora.

### Attivazione

Due passi: (1) il sistema genera un segreto *in attesa* e lo mostra (URI `otpauth://`, chiave testuale e codice QR); (2) l'attivazione avviene solo dopo aver inserito un codice valido generato da quel segreto. Alla conferma:
il timbro di sicurezza cambia (le altre sessioni dell'account sono invalidate), la sessione corrente viene rinnovata e vengono mostrati **una sola volta** 10 codici di recupero.

Per cambiare dispositivo con la 2FA già attiva servono password **e** un codice attuale.

### Codici di recupero

- 10 codici monouso da 16 caratteri Base32 (80 bit, in gruppi di 4), generati con un generatore crittografico.
- Salvati solo come hash SHA-256 (sono ad alta entropia: un hash veloce è adeguato e permette il confronto indicizzato). Ogni codice si consuma con un aggiornamento atomico (`UsatoIlUtc IS NULL`).
- Si possono rigenerare (servono password e un codice valido): i vecchi codici vengono invalidati.
- Un codice di recupero può sostituire il codice TOTP al login, con gli stessi limiti di tentativi.

### Obbligatorietà

Opzione `Sicurezza:DueFattori:Obbligo` con valori `Nessuno`, `PlatformAdmin`, `Amministratori`. **Predefinito `Amministratori`**: la 2FA è obbligatoria per `PlatformAdmin` e per ogni `TenantAdmin`. Chi non l'ha ancora attivata può accedere con la sola password ma, finché non l'ha configurata, ogni pagina dell'admin lo rimanda alla pagina di configurazione (come per il cambio password temporanea; il cambio password ha precedenza). Il valore `Nessuno` esiste per sviluppo e test, e non è consigliato in produzione (all'avvio viene registrato un avviso). Il ruolo `Utente` (acquirente) non usa l'admin e resta fuori ambito.

Con l'obbligo attivo non è possibile disattivare la 2FA. Se non è richiesta, un account può disattivarla con password e codice.

### Accesso con 2FA

1. Password corretta → nessuna sessione ancora: si emette un cookie di sfida a vita breve (5 minuti), `HttpOnly`, `SameSite=Strict`, cifrato con Data Protection, che contiene account, timbro di sicurezza e tenant dell'host e non vale su altri host.
2. Pagina `/admin/accesso/verifica`: codice TOTP o codice di recupero, con rate limiting della politica "accesso". Ogni codice errato incrementa **lo stesso contatore atomico di tentativi falliti** della password e porta allo stesso blocco: il secondo fattore non è attaccabile a forza bruta più facilmente della password.
3. Solo a verifica riuscita nasce la sessione. Il cookie di sfida non è una sessione e non dà accesso a nessuna pagina.

### Recupero (dispositivo perso)

Ordine di preferenza, ognuno nel registro di audit:

1. **Codici di recupero** (l'utente stesso).
2. **Reimpostazione da parte del `PlatformAdmin`** per i `TenantAdmin` (dal dettaglio del tenant): azzera la 2FA dell'account, cambia il timbro (sessioni chiuse) e al successivo accesso impone una nuova configurazione. L'operatore deve verificare l'identità della persona fuori banda (procedura nel runbook).
3. **Comando operativo `reset-2fa <email>`** per un `PlatformAdmin` che ha perso dispositivo e codici: eseguibile solo da chi ha accesso all'host del server e al database (stesso livello di privilegio di `migra` e `seed`); agisce solo su account `PlatformAdmin`.

Il **recupero password non disattiva né aggira la 2FA**: dopo il recupero della password si ripresenta il secondo fattore. Un'email compromessa non basta quindi a prendere un account. Se la chiave `Sicurezza:ChiaveSegreti` va persa i segreti TOTP non sono più leggibili: tutti gli account con 2FA vanno reimpostati (riportato nel runbook di backup).

## Conseguenze

- Nuove colonne su `Utenze` e tabella `CodiciRecuperoUtenza`; nuove azioni di audit (`AttivazioneDueFattori`, `DisattivazioneDueFattori`, `ReimpostazioneDueFattori`, `UsoCodiceRecupero`, `RigenerazioneCodiciRecupero`).
- Il flusso di accesso passa da un passo a due: `RisultatoAccesso` può richiedere la verifica del secondo fattore.
- Test e ambienti di sviluppo impostano `Sicurezza:DueFattori:Obbligo=Nessuno`; i test dedicati lo attivano.
- Rischio residuo accettato: un amministratore che perde sia dispositivo sia codici dipende da un `PlatformAdmin` (tenant) o da un operatore (piattaforma); è voluto, perché ogni scorciatoia sarebbe una scorciatoia per l'attaccante.

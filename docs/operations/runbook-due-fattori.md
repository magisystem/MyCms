# Runbook — Autenticazione a due fattori

Decisioni: [ADR-0019](../decisions/ADR-0019-two-factor-authentication.md). Descrizione tecnica: [`security.md`](../architecture/security.md#autenticazione-a-due-fattori-g-006).

## Per chi amministra: configurare la 2FA (enrollment)

1. Accedere con email e password. Se la 2FA è obbligatoria per il proprio ruolo e non è ancora configurata, si viene portati direttamente a **Verifica in due passaggi** (`/admin/sicurezza`); altrimenti la pagina si apre dal menu utente.
2. Premere **Configura**. Compare un codice QR e, sotto, la chiave testuale (da usare se non si può inquadrare il QR). Sono mostrati **una sola volta**: ricaricando la pagina non ricompaiono (basta premere di nuovo "Configura").
3. Nell'app di autenticazione (Microsoft Authenticator, Google Authenticator, un gestore di password…) aggiungere un account inquadrando il QR o inserendo la chiave (tipo: basato sul tempo; 6 cifre; 30 secondi).
4. Inserire il codice a 6 cifre mostrato dall'app e premere **Conferma e attiva**.
5. Compaiono **10 codici di recupero**: salvarli subito in un luogo sicuro e separato dal telefono (gestore di password, stampa in cassaforte). Non si possono rivedere; si possono rigenerare.

All'attivazione le altre sessioni dell'account vengono chiuse; quella in corso resta valida.

## Accedere

Email e password → pagina **Verifica in due passaggi** → codice a 6 cifre dell'app **oppure** un codice di recupero (si usa una volta sola; maiuscole, trattini e spazi non contano). Il passaggio scade dopo 5 minuti: poi si ricomincia dalla password. Cinque codici o password sbagliati, in totale, bloccano l'account per 5 minuti.

## Cambiare telefono o perdere solo l'app

- **Con il vecchio dispositivo funzionante:** `/admin/sicurezza` → "Cambia dispositivo" (servono password e un codice attuale), poi si ripete la configurazione. Il vecchio dispositivo smette di funzionare alla conferma e vengono generati nuovi codici di recupero.
- **Senza il vecchio dispositivo ma con i codici di recupero:** accedere con un codice di recupero e fare "Cambia dispositivo" usando un altro codice di recupero nel campo "Codice".
- **Senza dispositivo né codici:** vedere sotto.

Rigenerare i codici: `/admin/sicurezza` → "Rigenera" (password e codice). Controllare il numero di codici residui nella stessa pagina; rigenerare quando ne restano pochi.

## Dispositivo e codici persi

### Un `TenantAdmin` (a cura del `PlatformAdmin`)

1. **Verificare l'identità fuori banda** (telefonata a un numero già noto, richiesta dal responsabile del cliente, ecc.): la richiesta arriva spesso da chi ha perso l'accesso, e un attaccante potrebbe presentarsi proprio così. Non fidarsi di un'email non verificata.
2. Aprire il dettaglio del tenant → account → **Reimposta 2FA**.
3. L'azione azzera segreto e codici di recupero, chiude le sessioni dell'account e registra `ReimpostazioneDueFattori` nell'audit (chi, quando, quale account).
4. Comunicare all'interessato che al prossimo accesso con la password dovrà configurare di nuovo la 2FA (se ha dimenticato anche la password: prima il recupero password, che **non** aggira la 2FA).

### Un `PlatformAdmin` (a cura dell'operatore del server)

1. Verificare l'identità fuori banda come sopra; l'operatore deve avere accesso al server e alle sue variabili di ambiente (come per `migra` e `seed`).
2. Dal server, con la stessa configurazione dell'applicazione (stringa di connessione, `Sicurezza:ChiaveSegreti` non necessaria per questo comando):
   ```bash
   dotnet DvCms.Web.dll reset-2fa admin@esempio.it
   ```
   Codice di uscita 0 se riuscito; 1 se non esiste un `PlatformAdmin` con quell'email (il comando non agisce mai su altri ruoli) o se fallisce.
3. Il comando azzera segreto e codici, chiude le sessioni e scrive `ReimpostazioneDueFattori` nell'audit (senza attore, dettaglio "comando operativo"). Annotare nel registro operativo chi l'ha richiesto e chi l'ha eseguito.
4. Il `PlatformAdmin` accede con la password e configura di nuovo la 2FA.

## Dopo un ripristino o un cambio di chiave

I segreti TOTP sono cifrati con `Sicurezza:ChiaveSegreti`. Se il database viene ripristinato **con la stessa chiave** non serve fare nulla. Se la chiave è andata persa o è cambiata, nessun codice TOTP viene più accettato (l'avvio non fallisce; nel log compare "Segreto TOTP non leggibile"): reimpostare gli account con le procedure sopra (i `PlatformAdmin` con `reset-2fa`; poi, da un `PlatformAdmin` che ha riconfigurato la 2FA, i `TenantAdmin`). Per questo la chiave va conservata separatamente dal backup del database ([runbook di backup](backup-e-ripristino.md)).

## Configurazione

| Impostazione | Predefinito | Effetto |
|---|---|---|
| `Sicurezza:DueFattori:Obbligo` | `Amministratori` | `Amministratori` = `PlatformAdmin` e `TenantAdmin`; `PlatformAdmin` = solo il primo; `Nessuno` = facoltativa (sviluppo e test; fuori da Development all'avvio compare un avviso) |
| `Sicurezza:DueFattori:Emittente` | `DvCms` | Nome mostrato nell'app di autenticazione |
| `Sicurezza:ChiaveSegreti` | — | Obbligatoria per configurare la 2FA (32 byte in base64); senza, la configurazione è rifiutata con un messaggio e l'avvio segnala un avviso se l'obbligo è attivo |

## Cosa guardare nei log e nell'audit

- Audit: `AttivazioneDueFattori`, `DisattivazioneDueFattori`, `RigenerazioneCodiciRecupero`, `UsoCodiceRecupero` (con i codici rimasti: un uso inatteso è un campanello d'allarme), `ReimpostazioneDueFattori`.
- Log: "Account bloccato per 5 minuti dopo troppi tentativi falliti" (password o codici), "Segreto TOTP non leggibile".

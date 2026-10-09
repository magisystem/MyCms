# ADR-0016 — Autenticazione locale propria, senza ASP.NET Core Identity

- **Stato:** Accepted
- **Data:** 2026-10-09
- **Aggiornato:** 2026-10-09 (hash password con BCrypt; tabella `Utenze`; blocco di 5 minuti)
- **Supera:** la scelta di ASP.NET Core Identity in ADR-0011 (restano validi ruoli, regole sugli account, tenant dall'URL e recupero password)

## Contesto

ADR-0011 prevedeva ASP.NET Core Identity. Il committente preferisce account locali con tabelle e gestione proprie ("alla vecchia"), senza lo schema e il modello di gestione delle identità di ASP.NET Core Identity. Il progetto ha inoltre un requisito che Identity gestisce male: l'email è unica per tenant e ripetibile fra tenant, con il tenant deciso dall'host.

## Decisione

- Non si usa ASP.NET Core Identity: nessun pacchetto `Microsoft.AspNetCore.Identity.*` o `Microsoft.Extensions.Identity.*`, nessuna tabella `AspNet*`.
- **Tabella `Utenze` propria** (nome italiano, vedi `domain-model.md`; entità `User` nel codice). Campi indicativi: `Id` (Guid), `TenantId` (nullable), `Role` (`PlatformAdmin`, `TenantAdmin`, `Utente`), `Email`, `NormalizedEmail`, `DisplayName`, `PasswordHash`, `PasswordUpdatedAtUtc`, `MustChangePassword`, `SecurityStamp`, `Status` (attivo/disabilitato), `FailedLoginCount`, `LockoutUntilUtc`, `LastLoginAtUtc`, date di creazione/modifica, token di concorrenza.
  - Il ruolo è una colonna: i ruoli sono quattro e fissi, `Guest` non è un account. Nessuna tabella di ruoli, claim o login esterni.
  - Vincolo: `PlatformAdmin` ⇔ `TenantId` nullo; `TenantAdmin` e `Utente` hanno `TenantId` obbligatorio.
  - Indice univoco su `(TenantId, NormalizedEmail)`: l'email è unica nel tenant e ripetibile fra tenant. Fra i `PlatformAdmin` (`TenantId` nullo) l'email è unica perché in SQL Server i NULL sono considerati uguali.
- **Sessione:** cookie authentication del framework ASP.NET Core (non fa parte di Identity). Il cookie contiene `UserId`, ruolo, `TenantId` e `SecurityStamp`. A ogni richiesta si verifica che il tenant dell'host sia coerente, che l'account sia attivo e che lo `SecurityStamp` corrisponda: cambio password, disabilitazione o reset invalidano le sessioni. Schemi di cookie distinti per area admin e area cliente (ADR-0011). Il cookie è protetto con Data Protection, quindi il key ring va condiviso fra le istanze.
- **Password:** hash con **BCrypt**, tramite la libreria `BCrypt.Net-Next` (candidata; licenza e manutenzione da verificare prima dell'aggiunta, regola 11 di `AGENTS.md`; il pacchetto è limitato a `DvCms.Infrastructure`). Si usa la variante "enhanced" (pre-hash SHA-384) perché BCrypt tronca l'input a 72 byte; lunghezza massima della password limitata (ad esempio 128 caratteri) per evitare costi eccessivi. Work factor di partenza 12, configurabile; l'hash contiene già algoritmo, costo e sale, e al login, se il work factor configurato è maggiore, l'hash viene rigenerato in modo trasparente. Verifica con la funzione della libreria (a tempo costante). Nessuna password in chiaro in log o database. BCrypt serve solo per le password; i segreti dei canali email (ADR-0015) sono cifrati in modo reversibile con Data Protection.
- **Login:** l'account si cerca solo nel tenant dell'host più i `PlatformAdmin` globali; errore uniforme per credenziali errate, account inesistente, account di un altro tenant, account bloccato o disabilitato; **blocco di 5 minuti dopo 5 tentativi falliti consecutivi** (soglia da confermare), con contatore azzerato al primo accesso riuscito; rate limiting sugli endpoint di login e recupero.
- **Autorizzazione:** policy di ASP.NET Core basate su claims creati dal nostro caso d'uso di accesso, applicate nell'application layer (ADR-0011).
- **Recupero password, inviti e cambio password** usano tabelle e casi d'uso propri (`PasswordRecoveryRequest`, ADR-0011), con email dagli adapter di ADR-0015.
- **Two-factor authentication** (ultima priorità): sarà trattata con un ADR dedicato (TOTP, libreria o implementazione RFC 6238).
- Restano usati i componenti del framework estranei a Identity: cookie authentication, antiforgery, Data Protection, rate limiting.

## Alternative considerate

- **PBKDF2 con le primitive di .NET** (nessuna dipendenza): era la proposta iniziale; sostituita da BCrypt per scelta del committente.
- **Argon2id:** richiederebbe comunque un pacchetto esterno; non scelto.
- **ASP.NET Core Identity con store personalizzato:** respinta per scelta del committente (schema e modello di gestione delle identità) e perché richiederebbe comunque di riscrivere la ricerca degli utenti per tenant.
- **Identity con tabelle rinominate:** non risolve il modello di gestione.
- **Identity provider esterno (OIDC):** rimandato finché non ci sono requisiti SSO.

## Conseguenze

- Più codice di sicurezza da scrivere, mantenere e testare: hashing, blocco account, sessioni, recupero password, cambio password. Mitigazioni: primitive standard di .NET, nessun algoritmo di hashing inventato, test mirati (hash, lockout, invalidazione sessioni, errore uniforme), inclusione dell'autenticazione nella security review (G-005).
- Non si ha nulla di pronto per conferma email, 2FA o accesso con provider esterni: si implementeranno solo se serviranno.
- Scompare il rischio tecnico "Identity multi-tenant" del piano M0/M1: l'unicità per tenant è un normale indice del database.
- La migrazione futura verso un identity provider esterno resta possibile perché l'accesso è dietro casi d'uso dell'application layer.

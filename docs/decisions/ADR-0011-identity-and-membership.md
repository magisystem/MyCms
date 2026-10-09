# ADR-0011 — Identity e autorizzazione tramite membership

- **Stato:** Proposed
- **Data:** 2026-10-09

## Contesto

Un utente può lavorare su tenant e siti multipli con ruoli differenti. Il ruolo deve essere collegato al contesto e alla risorsa, non soltanto a un claim globale.

## Decisione proposta

Partire da ASP.NET Core Identity per gli account locali, con tabelle membership esplicite tra utente e tenant/sito. Centralizzare policy autorizzative e usare ruoli suggeriti in `security.md`. SSO/OIDC può essere aggiunto quando richiesto.

## Alternative considerate

- Ruolo globale sull'utente per tutti i tenant: rifiutato perché non esprime appartenenze differenti.
- Identity provider esterno obbligatorio dal day one: rimandato finché requisiti SSO non sono chiari.

## Conseguenze

Le operazioni amministrative devono verificare membership + permessi della risorsa. L'accesso globale di supporto è un privilegio separato e auditato.

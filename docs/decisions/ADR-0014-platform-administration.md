# ADR-0014 — Amministrazione di piattaforma: tenant, domini e certificati gestiti manualmente

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

I clienti non si registrano da soli: tenant, siti e utenti sono creati dagli operatori della piattaforma. Il numero di tenant è contenuto e i domini cambiano raramente. Automatizzare verifica dei domini e certificati TLS sarebbe costo senza beneficio immediato.

## Decisione

- Esiste un'area di amministrazione di piattaforma (Razor Area dedicata) accessibile solo al ruolo **PlatformAdmin**, per creare e gestire tenant, siti, domini e utenti `TenantAdmin`.
- Nessuna registrazione self-service: non esistono pagine o endpoint pubblici di sign-up.
- Solo il PlatformAdmin può creare utenti `TenantAdmin` o elevare un utente a `TenantAdmin`. Il controllo è applicato lato server nel caso d'uso, non solo nella UI. Un TenantAdmin non può assegnare né auto-assegnarsi il proprio ruolo o uno superiore.
- Il primo PlatformAdmin viene creato da un comando di bootstrap/seed documentato, eseguito dall'operatore, non da una pagina web. Il comando non accetta credenziali predefinite.
- **Verifica dei domini:** manuale. Il PlatformAdmin registra il dominio dopo averne verificato il possesso fuori banda. `SiteDomain` conserva chi ha verificato e quando; un host risolve solo se il dominio è registrato, verificato e attivo.
- **Certificati TLS:** gestiti manualmente a livello di reverse proxy/hosting. L'applicazione non emette né rinnova certificati e non implementa ACME.
- Ogni operazione dell'area di piattaforma (creazione/sospensione tenant, domini, utenti, elevazione ruoli) viene registrata in `AuditLog` con attore e motivo.

## Alternative considerate

- Onboarding self-service con verifica DNS automatica ed ACME: rimandato finché i clienti non si registreranno da soli.
- Creazione dei tenant solo da script/DB: poco verificabile e non auditabile.

## Conseguenze

- Il flusso operativo (richiesta dominio, verifica, certificato, registrazione) va descritto nel runbook (`docs/architecture/operations.md`).
- Errori sui domini o scadenza dei certificati sono rischio operativo del team: servono promemoria e controlli periodici.
- Se il modello cambia (clienti autonomi), serve un nuovo ADR che sostituisca questo.

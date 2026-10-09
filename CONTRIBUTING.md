# Contribuire

## Prima di iniziare

- Leggi `AGENTS.md` e la documentazione pertinente.
- Controlla gli ADR prima di aggiungere una tecnologia o cambiare un confine di modulo.
- Per cambi architetturali, crea o aggiorna un ADR.

## Pull request

Una PR dovrebbe risolvere un problema circoscritto e includere test. Non combinare, per esempio, tenancy, page builder e checkout in un'unica PR.

Checklist minima:

- [ ] Il comportamento è descritto e i criteri di accettazione sono soddisfatti.
- [ ] I test automatici pertinenti sono stati eseguiti.
- [ ] Le nuove query/entità sono tenant-safe.
- [ ] Sono stati controllati autorizzazioni, input non attendibili e dati personali.
- [ ] La documentazione è stata aggiornata.
- [ ] Migrazioni e downgrade/rollback sono stati valutati.
- [ ] Non sono stati aggiunti segreti, dati reali o file generati non necessari.
- [ ] I risultati dei test sono riportati correttamente.

## Convenzioni commit

Messaggi brevi, all'imperativo, preferibilmente con prefisso: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `build:`.

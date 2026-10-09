# Definition of Done

Una funzionalità è completata solo se i punti applicabili sono veri:

- [ ] Comportamento e acceptance criteria soddisfatti.
- [ ] Logica di dominio non collocata nella view o nel PageModel oltre all'orchestrazione minima.
- [ ] Validazione lato server per tutti gli input non attendibili.
- [ ] Autenticazione e autorizzazione verificate.
- [ ] Isolamento tenant verificato con test automatici per ogni nuova risorsa tenant-scoped.
- [ ] Test unitari/integration/e2e aggiunti in base al rischio.
- [ ] Build e test pertinenti eseguiti, esito registrato correttamente.
- [ ] Error handling, logging e CancellationToken curati dove appropriato.
- [ ] Migrazioni DB create e controllate; strategia di rollback valutata.
- [ ] Nessun segreto o dato personale sensibile nei log.
- [ ] Documentazione aggiornata e gli ADR modificati quando necessario.
- [ ] Accessibilità base e comportamento mobile verificati per le UI interessate.
- [ ] Nessun TODO critico lasciato senza issue collegata.

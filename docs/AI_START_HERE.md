# Prompt iniziale per un agente AI

Copia il testo seguente nella sessione dell'agente dopo aver aggiunto questa documentazione al repository.

---

Leggi integralmente `AGENTS.md`, `README.md`, `docs/architecture/overview.md`, `docs/architecture/multi-tenancy.md`, gli ADR pertinenti e `docs/delivery/roadmap.md`.

Non implementare subito tutte le funzionalità. Prima ispeziona il repository e riferisci:

1. stato attuale e file/progetti presenti;
2. eventuali conflitti tra il repository e la documentazione;
3. piano per la sola milestone M0/M1;
4. test che verranno aggiunti;
5. decisioni aperte che bloccano il lavoro.

Fermati e attendi l'approvazione del piano. Solo dopo, implementa la prima vertical slice: solution .NET 10 compilabile, SQL Server/EF Core, tenant/site/domain, risoluzione tenant da host registrato, ruoli `PlatformAdmin`/`TenantAdmin`/`Utente`/`Guest` con area di piattaforma minima, layout admin Tabler, una pagina draft/published renderizzata da Razor e test d'isolamento cross-tenant. Non implementare ancora e-commerce o builder completo.

Prima di concludere esegui i test possibili, riporta comandi e risultati effettivi, aggiorna la documentazione e indica quali criteri di accettazione sono rimasti aperti. Non dichiarare test superati senza averli eseguiti. Non cambiare gli ADR senza spiegare il motivo e proporre una modifica documentata.

---

# DvCms — CMS SaaS multi-tenant: documentazione architetturale

Questa cartella contiene la documentazione iniziale di **DvCms** (namespace e progetti `DvCms.*`), un CMS multi-tenant sviluppato in .NET, pensato per siti di clienti con temi Razor dedicati, editor visuale ibrido ed e-commerce integrato.

## Obiettivo

Fornire a sviluppatori e agenti AI (Codex, Claude Code, Gemini, DeepSeek o strumenti analoghi) una fonte di verità versionabile nel repository e indicazioni sufficienti per implementare il sistema per incrementi verificabili.

## Scelte architetturali iniziali

- **Backend e sito pubblico:** ASP.NET Core, C#, Razor Pages/MVC.
- **Interfaccia amministrativa:** Razor Pages con navigazione e form HTML tradizionali, tema **Tabler** e icone **Tabler Icons** (cartella `admin-theme/`, ADR-0013). HTMX non è obbligatorio; si aggiunge solo se una schermata beneficia realmente di aggiornamenti parziali.
- **Page builder:** GrapesJS in una pagina Razor dedicata, con asset JavaScript isolati. Nessun backend Node.js.
- **Runtime:** .NET 10 LTS e Entity Framework Core 10, mantenuti sulle ultime patch supportate.
- **Persistenza:** Microsoft SQL Server con EF Core 10, una sola serie di migrazioni (ADR-0012).
- **Architettura:** monolite modulare, non microservizi.
- **Tenancy:** database condiviso con isolamento logico rigoroso per tenant e sito.
- **Ruoli:** `PlatformAdmin`, `TenantAdmin`, `Utente` (acquirente dell'e-commerce), `Guest` (visitatore del sito, per tenant) (ADR-0011). Solo il PlatformAdmin crea tenant e utenti `TenantAdmin`.
- **Onboarding:** nessuna registrazione self-service per gli account amministrativi; tenant e domini sono gestiti manualmente dagli operatori e i certificati TLS dall'hosting tramite reverse proxy (ADR-0014). La registrazione esiste solo per gli acquirenti dei siti con e-commerce.
- **Temi:** pacchetti Razor fidati e versionati, distribuiti con l'applicazione; i clienti non caricano assembly o viste Razor.
- **Contenuti:** documenti JSON tipizzati e versionati; il solo HTML esportato non è la fonte di verità.
- **E-commerce:** modulo interno al CMS, separato nei confini di dominio.

SQL Server è l'unico database supportato. I dettagli sono in [`docs/architecture/database.md`](docs/architecture/database.md).

## Come usare questa documentazione

1. Leggere [`AGENTS.md`](AGENTS.md) prima di implementare codice.
2. Leggere [`docs/architecture/overview.md`](docs/architecture/overview.md) e gli ADR rilevanti.
3. Consultare [`docs/delivery/roadmap.md`](docs/delivery/roadmap.md) e [`docs/delivery/backlog.md`](docs/delivery/backlog.md) per ordinare il lavoro.
4. Implementare una milestone alla volta, con test e documentazione aggiornata.
5. Prima del rilascio pubblico, completare le attività di sicurezza e i test di isolamento tenant.

## Stato del documento

Questa è una **baseline architetturale**, non un'applicazione già implementata. Le decisioni contrassegnate come *da decidere* devono essere approvate prima di sviluppare le parti che ne dipendono. Non inventare provider commerciali, regole fiscali o requisiti contrattuali.

## Indice

- [Visione prodotto](docs/product/product-vision.md)
- [Architettura](docs/architecture/overview.md)
- [Stack](docs/architecture/technology-stack.md)
- [Database SQL Server](docs/architecture/database.md)
- [Modello di dominio e dati](docs/architecture/domain-model.md)
- [Multi-tenancy](docs/architecture/multi-tenancy.md)
- [Temi e rendering](docs/architecture/themes-and-rendering.md)
- [Page builder](docs/architecture/page-builder.md)
- [Pubblicazione e SEO](docs/architecture/publishing-and-seo.md)
- [E-commerce](docs/architecture/ecommerce.md)
- [Sicurezza](docs/architecture/security.md)
- [Operatività](docs/architecture/operations.md)
- [Test](docs/architecture/testing.md)
- [Roadmap](docs/delivery/roadmap.md)
- [Backlog](docs/delivery/backlog.md)
- [Piano M0/M1](docs/delivery/plan-m0-m1.md)
- [Setup di sviluppo locale](docs/development/local-setup.md)
- [Convenzioni di codice](docs/development/convenzioni-codice.md)
- [Definition of Done](docs/delivery/definition-of-done.md)
- [Questioni aperte](docs/open-questions.md)
- [ADR](docs/decisions/README.md)

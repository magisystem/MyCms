# Architettura generale

## Decisione

Il prodotto parte come monolite modulare ASP.NET Core. Il sito pubblico, l'admin e le API vengono distribuiti dalla stessa applicazione o dallo stesso prodotto di deployment; worker e servizi gestiti possono essere separati quando utile, ma non sono microservizi di dominio per default.

## Componenti

La persistenza usa SQL Server come unico database supportato ed è descritta in [database.md](database.md).

- **Public web:** routing host/slug, pagine pubblicate, Razor layout, componenti e SEO.
- **Admin web:** Razor Pages con tema Tabler, form HTML, autenticazione, CRUD, workflow e configurazioni. Include l'**area di piattaforma** (solo `PlatformAdmin`) per tenant, siti, domini e utenti `TenantAdmin`.
- **Page builder:** asset JS separato e caricato solo nelle pagine di editing visuale; comunica con endpoint ASP.NET Core.
- **Application layer:** casi d'uso, autorizzazioni, validazioni, transizioni di stato e orchestrazione.
- **Domain modules:** tenancy, sites, content, templates, themes, media, SEO e commerce.
- **Infrastructure:** EF Core con SQL Server, storage, cache, email e provider esterni.
- **Worker:** outbox, email, webhook asincroni, immagini e sitemap.

## Diagramma logico

```text
Browser
  ├── Public site ──> ASP.NET Core / Razor ──> Application ──> Domain modules
  ├── Admin pages ──> ASP.NET Core / Razor ──> Application ──> Domain modules
  └── Visual builder ──> Admin Razor shell ──HTTP──> Admin API
                                                │
                                      SQL Server / Object storage
                                                │
                                      Worker + Outbox + provider esterni
```

## Regole di dipendenza

- Domain non dipende da ASP.NET Core, EF Core o provider esterni.
- Application dipende da Domain e definisce i contratti necessari.
- Infrastructure implementa persistenza e integrazioni.
- Web compone le dipendenze e rende UI/API.
- Themes dipendono da contratti stabili del rendering, non accedono direttamente al database.
- Il modulo Commerce non dipende dal rendering Razor; espone operazioni e modelli applicativi.

Le dipendenze fisiche tra assembly possono essere semplificate all'inizio. I confini logici devono però restare riconoscibili e verificabili.

## Flussi principali

### Richiesta pubblica

1. Normalizzare e verificare host/proxy.
2. Risolvere dominio registrato → sito → tenant.
3. Cercare la revisione pubblicata per slug e lingua se applicabile.
4. Risolvere tema/versione e configurazione del sito.
5. Renderizzare il documento con renderer consentiti.
6. Restituire HTML, metadati SEO e header di cache appropriati.

### Modifica e pubblicazione

1. Autenticare l'utente e verificare ruolo e tenant dell'account.
2. Caricare draft e policy di modifica.
3. Salvare con controllo di concorrenza.
4. Validare lato server il progetto/dati aggiornati.
5. Alla pubblicazione creare una revisione immutabile e aggiornare il puntatore pubblicato in transazione.
6. Registrare eventi/outbox e invalidare le cache dipendenti.

## Struttura solution raccomandata

Solution `DvCms.sln`; progetti e namespace con prefisso `DvCms.`.

```text
src/
  DvCms.Web/             (include wwwroot/lib/tabler e wwwroot/lib/tabler-icons)
  DvCms.Domain/
  DvCms.Application/
  DvCms.Infrastructure/
  DvCms.Themes.Abstractions/
  DvCms.Themes.Corporate/
  DvCms.Themes.Creative/
  DvCms.Store/
  DvCms.Worker/
  frontend/page-builder/
tests/
  DvCms.UnitTests/
  DvCms.IntegrationTests/
  DvCms.TenantIsolationTests/
```

Non è necessario creare tutti questi progetti il primo giorno. Iniziare con pochi assembly e separare solo quando migliora i confini o i test.

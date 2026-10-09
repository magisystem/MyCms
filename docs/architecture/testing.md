# Strategia di test

## Livelli

### Unit test

Testare regole di dominio pure: transizioni stato, validazioni componenti, policy template, slug e calcoli business isolati.

### Integration test

Usare SQL Server reale per la suite d'integrazione primaria e PostgreSQL reale per la suite di compatibilità. Entrambe devono verificare query, vincoli, migrazioni, transazioni e tenancy. EF Core InMemory e SQLite non sostituiscono i test sui provider effettivamente supportati.

### End-to-end

Verificare flussi critici: login, creazione pagina, save draft, preview, publish, cambio slug, aggiunta prodotto, carrello e pagamento simulato.

### Test contrattuali del builder

- caricare e risalvare un progetto editor;
- conversione editor project → render document;
- round-trip per i componenti supportati;
- rifiuto di componenti/proprietà sconosciuti;
- rispetto di template locks e allowed slots;
- rendering coerente con tema;
- compatibilità/migrazione dei documenti versionati.

## Tenant isolation matrix

Creare almeno Tenant A e Tenant B e tentare per tutte le aree di leggere, modificare, cancellare e collegare risorse dell'altro tenant. Includere API, pagine admin, media, cache, job/outbox, prodotti, carrelli e ordini.

## Test sicurezza

- IDOR e tenant switch;
- CSRF sulle mutazioni;
- XSS persistente in rich text/component properties;
- upload malevoli/oversize;
- manipolazione di prezzo e quantità;
- webhook duplicati o firma non valida;
- concorrenza su revisioni, inventario e checkout.

## Test di rendering/SEO

- HTML principale disponibile senza JavaScript;
- canonical, title, description, robots e sitemap;
- status code corretti (200/301/404);
- tema e asset corretti in base al dominio;
- nessuna revisione draft mostrata pubblicamente.

## Regole CI

La pipeline deve eseguire restore, build, unit tests, integration tests e analisi dipendenze disponibili. SQL Server è obbligatorio per tutte le suite DB; PostgreSQL è obbligatorio per ogni PR che modifica persistenza, schema, query o flussi critici e deve girare regolarmente nella pipeline completa. Entrambe le serie di migrazioni devono applicarsi a un database vuoto prima del deploy. Non considerare un test saltato come un test passato.

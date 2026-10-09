# Strategia di test

## Livelli

### Unit test

Testare regole di dominio pure: transizioni stato, validazioni componenti, policy template, slug e calcoli business isolati.

### Integration test

Usare SQL Server reale (container o istanza effimera) per la suite d'integrazione: query, vincoli, migrazioni, transazioni e tenancy. EF Core InMemory e SQLite non sostituiscono i test su SQL Server.

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

## Test di localizzazione

- Chiave nuova: creazione delle righe per tutte le lingue attive (italiano `DaTradurre` falso, le altre vero) e restituzione del testo italiano.
- Chiave esistente in una lingua: restituito il testo della lingua; mancante in una lingua: riga creata con il testo italiano.
- Creazione concorrente della stessa chiave senza errori né duplicati.
- Modifica da parte del `PlatformAdmin`: azzera `DaTradurre`, invalida la cache, scrive in `RegistroAudit`; `TenantAdmin` e altri ricevono 403/404.
- Output codificato in HTML; verifica che gli argomenti di `_L[...]` siano costanti letterali.
- Pagine in lingua: stesso `IdGruppoTraduzione`, slug univoco per sito e lingua, 404 se manca la lingua, `hreflang` corretto; traduzioni di altre entità con ripiego sulla lingua predefinita del sito.

## Tenant isolation matrix

Creare almeno Tenant A e Tenant B e tentare per tutte le aree di leggere, modificare, cancellare e collegare risorse dell'altro tenant. Includere API, pagine admin, media, cache, job/outbox, prodotti, carrelli e ordini.

## Test sicurezza

- IDOR e tenant switch;
- escalation di ruolo: solo il `PlatformAdmin` crea/eleva `TenantAdmin`; `TenantAdmin` e `Utente` non possono assegnarsi ruoli superiori né accedere al menu "Platform"; la registrazione non può scegliere il ruolo né funzionare su siti senza e-commerce;
- recupero password: nessuna enumerazione account, OTP scaduto/errato/riusato, limite tentativi, link monouso riusato o scaduto, invalidazione sessioni;
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

La pipeline deve eseguire restore, build, unit tests, integration tests e analisi dipendenze disponibili. Le suite DB girano su SQL Server e le migrazioni devono applicarsi a un database vuoto prima del deploy. Non considerare un test saltato come un test passato.

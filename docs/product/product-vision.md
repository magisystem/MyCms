# Visione prodotto

## Scopo

Realizzare un CMS SaaS multi-tenant in cui un operatore della piattaforma può gestire più clienti, ciascuno con uno o più siti e domini, temi grafici dedicati, pagine modificabili e, dove abilitato, e-commerce.

## Utenti

- **PlatformAdmin:** operatore della piattaforma. Dal menu "Platform" crea e gestisce tenant, siti, domini e utenti `TenantAdmin`; entra nell'admin di qualunque tenant e vi lavora come un `TenantAdmin`.
- **TenantAdmin:** amministratore di un cliente. Gestisce siti, tema, SEO, contenuti, media e, con l'e-commerce, catalogo e ordini.
- **Utente:** acquirente registrato di un sito con e-commerce; può solo emettere ordini. Nei siti senza e-commerce non esiste.
- **Guest:** visitatore del sito pubblico senza login, sempre legato al proprio tenant; vede tutto ciò che è pubblico. Non può emettere ordini.

Un `TenantAdmin` appartiene a un solo tenant (un tenant può averne più di uno); il `PlatformAdmin` accede a tutti. I clienti non si registrano da soli: gli account amministrativi sono creati dagli operatori (ADR-0014); solo gli acquirenti si registrano, e solo nei siti con e-commerce.

## Principi di prodotto

1. La maggioranza delle pagine deve essere creata e modificata con template guidati e campi semplici.
2. Deve essere disponibile una modalità visuale libera, limitata a componenti e capacità esplicitamente supportati.
3. I temi sono sviluppati dal team della piattaforma in C#/Razor.
4. I contenuti non devono essere legati a un'unica versione del tema o dell'editor.
5. Le pagine pubbliche devono essere renderizzate lato server e avere HTML significativo, URL canonici e metadati SEO.
6. Il contenuto di un tenant non è mai visibile a un altro tenant senza un'autorizzazione esplicita e verificata.
7. L'e-commerce è un modulo interno, con confini di dominio e controlli di sicurezza propri.
8. L'admin standard deve restare server-rendered; una SPA generale non è un obiettivo.

## Fuori dallo scope iniziale

- Marketplace pubblico di temi/plugin di terze parti.
- Esecuzione di codice C#, Razor o JavaScript arbitrario fornito dai clienti.
- Architettura a microservizi.
- App mobile nativa.
- Editor di temi grafici visuale completo.
- Più di due lingue, traduzione automatica, lingue da destra a sinistra e multi-valuta finché i requisiti non sono definiti. Italiano e inglese sono in scope (ADR-0018).
- Gestione fiscale transnazionale completa senza analisi e provider dedicati.

## Requisiti qualitativi iniziali

- Isolamento tenant verificabile con test automatici.
- Pagine pubbliche renderizzate server-side.
- Draft e published revision separati.
- Pubblicazione ripetibile e tracciabile.
- Backup e ripristino documentati.
- Nessun segreto nel repository.
- Aggiornamenti delle dipendenze e runtime supportati.

I target quantitativi per throughput, latenza, disponibilità e retention devono essere definiti prima della fase di produzione.

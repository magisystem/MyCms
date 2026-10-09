# Visione prodotto

## Scopo

Realizzare un CMS SaaS multi-tenant in cui un operatore della piattaforma può gestire più clienti, ciascuno con uno o più siti e domini, temi grafici dedicati, pagine modificabili e, dove abilitato, e-commerce.

## Utenti

- **Platform operator:** gestisce tenant, piani, domini e operazioni tecniche di piattaforma.
- **Tenant owner:** gestisce il cliente, gli utenti e i siti assegnati.
- **Site administrator:** configura un sito, il tema e le impostazioni SEO.
- **Editor:** modifica pagine e media.
- **Publisher:** revisiona e pubblica contenuti.
- **Store manager:** gestisce catalogo, inventario e ordini.
- **Visitor/customer:** naviga il sito pubblico ed eventualmente acquista.

Un utente può avere membership in più tenant, con ruoli diversi per ciascuno.

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
- Multilingua avanzato e multi-valuta finché i requisiti non sono definiti.
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

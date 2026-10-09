# Pubblicazione, routing e SEO

## Pagine e URL

- Risolvere il dominio registrato prima di cercare la pagina.
- URL canonico per ogni pagina pubblica.
- Slug univoco per sito e lingua. Ogni lingua di una pagina ha il proprio URL, titolo, descrizione e canonical; le versioni collegate (`IdGruppoTraduzione`) si dichiarano con `hreflang`; il prefisso di lingua (`/en/...`) fa parte dell'URL canonico. Una pagina inesistente nella lingua richiesta risponde 404 (ADR-0018).
- Gestire rename di slug con redirect 301 quando appropriato.
- Distinguere 404, redirect, preview e pagina pubblicata.
- Non indicizzare le pagine in bozza o le preview.

## Metadati SEO

Il modello pagina deve supportare titolo SEO, meta description, canonical, robots e dati social essenziali (Open Graph/Twitter). Aggiungere JSON-LD solo per tipologie supportate, generato da dati validati e coerenti con il contenuto visibile.

## Rendering e indicizzazione

Il server genera HTML semantico completo con heading appropriati, link reali, `lang` (della lingua della pagina), immagini con alt text e metadata. JavaScript è un enhancement, non un prerequisito per leggere il contenuto principale.

## Sitemap e robots

- Sitemap per dominio/sito, solo URL canonici pubblicati e indicizzabili.
- Aggiornare o invalidare lo stato sitemap al publish/unpublish/rename.
- `robots.txt` varia per dominio e ambiente; mai bloccare accidentalmente tutto in produzione o esporre preview.
- Sitemap non deve includere pagine di un altro tenant.

## Pubblicazione atomica

La revisione pubblicata è immutabile. Il publish aggiorna il puntatore live e registra un evento in una transazione; la cache si invalida dopo commit tramite outbox o meccanismo equivalente. Prevedere rollback a una revisione precedente.

## Performance e cache

- Cache pubblica per host/sito/path/revisione e per tutte le altre dimensioni che cambiano la risposta.
- Non cacheare come pubblico contenuti personali, carrello, checkout, account o preview privata.
- Asset statici con cache aggressiva e nomi versionati/hash.
- Lazy loading dove utile, senza ritardare inutilmente l'immagine principale LCP.
- Misurare le prestazioni; non impostare obiettivi numerici prima di definire profilo di carico.

## Checklist SEO per release

- [ ] Canonical corretto sul dominio richiesto.
- [ ] Title e description renderizzati.
- [ ] HTML principale presente senza JS.
- [ ] 404/301/robots/sitemap verificati.
- [ ] Nessuna bozza o URL cross-tenant indicizzato.
- [ ] Dati strutturati validi e basati su informazioni vere.
- [ ] Cambio slug preserva redirect e non crea loop.

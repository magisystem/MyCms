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

## Anteprima e rollback (C-004)

- **Anteprima:** `/admin/pagine/{id}/anteprima` (ultima bozza) e `?revisione={id}` (una revisione precisa). Sta sotto `/admin`: richiede la sessione admin del tenant dell'host, quindi un tenant diverso o un visitatore non la vede (404 / reindirizzamento all'accesso). Risponde sempre con `X-Robots-Tag: noindex, nofollow, noarchive`, `<meta name="robots" content="noindex, nofollow">`, `Cache-Control: no-store, private` e `Referrer-Policy: no-referrer`, e mostra una barra che dice che non è contenuto pubblico. Una revisione di un'altra pagina, sito o tenant non si apre. Con il contenuto in testo semplice, sempre codificato, l'origine condivisa con l'admin è sufficiente; quando arriverà il rich text l'anteprima passerà a un'origine isolata o a token di breve durata (security.md).
- **Rollback:** si può pubblicare qualunque revisione della pagina (anche precedente): il puntatore pubblicato si sposta in una transazione insieme all'evento di pubblicazione dell'outbox, e l'ultima bozza non cambia (la pagina risulta "con modifiche non pubblicate"). Serve il token di concorrenza della pagina, come per ogni pubblicazione. **Ripristino:** il contenuto di una revisione passata torna come *nuova* revisione (bozza), con la nota "Ripristino della revisione N": le revisioni restano immutabili.

## SEO base e redirect (C-005)

- **Metadati per revisione:** `TitoloSeo` (se vuoto si usa il titolo della pagina), `DescrizioneSeo` e `NonIndicizzare` fanno parte della revisione: cambiano solo con una nuova bozza e diventano pubblici con la pubblicazione, come il resto (ripristino, rollback e passaggio di modello li conservano). Il server li normalizza e ne limita lunghezza e caratteri (esito `DatiNonValidi` con i campi `TitoloSeo`/`DescrizioneSeo`).
- **Documento pubblico:** `<title>`, `<meta name="description">` (solo se presente), `<link rel="canonical">`, Open Graph (`og:type/title/url/locale`, `og:description`) e `twitter:card` essenziali. Tutto è codificato da Razor. Niente JSON-LD per ora.
- **`noindex`:** una pagina con `NonIndicizzare` emette `<meta name="robots" content="noindex, follow">` e `X-Robots-Tag: noindex, follow`; non emette `hreflang` né selettore. Le anteprime restano `noindex, nofollow` senza canonical né Open Graph; la pagina 404 è `noindex, nofollow`.
- **Cambio di slug:** consentito anche per pagine pubblicate. Se la pagina è stata pubblicata almeno una volta, il vecchio slug diventa un reindirizzamento **301** (`RedirectPagina`) alla pagina, nella stessa lingua e nello stesso sito. Il redirect segue la pagina: dopo più rinomine tutti i vecchi indirizzi portano allo slug attuale con un solo salto. Se uno slug torna in uso (rinomina all'indietro, altra pagina, pagina nuova) il reindirizzamento relativo è tolto, quindi non si formano cicli; in ogni caso il servizio non reindirizza mai verso lo stesso percorso. Un redirect si segue solo se la pagina di destinazione è pubblicata (altrimenti 404) e non attraversa lingue, siti o tenant. Per una pagina pubblicata il cambio accoda l'evento `PaginaRinominata` nella stessa transazione (oggi il gestore registra e basta; la cache lo userà con G-002). L'admin elenca i reindirizzamenti di una pagina e permette di eliminarli.
- **Pagina iniziale:** ha un solo indirizzo canonico (`/` o `/en`); chiamarla con il suo slug risponde 301 verso la radice, e il vecchio slug di una pagina iniziale rinominata porta alla radice.

## Pagine in più lingue (C-008)

- **Lingue del sito:** tabella `LingueSito` (sito, lingua, `Predefinita`; chiave esterna composta `(IdTenant, IdSito)`, una sola predefinita per sito, indice univoco per sito e lingua). Ogni sito nasce con l'italiano predefinito; il `PlatformAdmin` abilita altre lingue attive, ne disabilita (non la predefinita né una che ha ancora pagine) e cambia la predefinita solo finché il sito non ha pagine pubblicate (cambierebbe tutti gli indirizzi). Gli stessi vincoli valgono se le righe mancano (non dovrebbe succedere): si ripiega sulla lingua predefinita della piattaforma.
- **URL:** la lingua predefinita non ha prefisso (`/slug`, la pagina iniziale è `/`); le altre sì (`/en/slug`, la pagina iniziale è `/en`). Il primo segmento è un prefisso solo se è una lingua abilitata non predefinita. Per questo i codici delle lingue (`it`, `en`, ...) **non sono utilizzabili come slug** (rifiutati alla creazione e al cambio di slug). Un solo segmento di slug, quindi `/en/a/b` e `/it/x` danno 404.
- **404 per lingua:** una pagina non esiste per una lingua finché non c'è una `Pagina` pubblicata in quella lingua: nessun ripiego automatico su un'altra lingua (`/en/solo-it` e `/solo-en` rispondono 404). Una lingua disabilitata smette di rispondere e sparisce da `hreflang` e selettore.
- **Traduzioni:** una `Pagina` per lingua, collegate da `IdGruppoTraduzione`; si crea una traduzione dalla pagina d'origine (`/admin/pagine/nuova?traduzioneDi=...`), in una lingua abilitata del sito che manca nel gruppo. Ogni lingua ha le proprie revisioni, il proprio slug e il proprio modello. L'edizione è per lingua: ogni pagina mostra le sue traduzioni e quelle che mancano.
- **Metadati per lingua:** `<html lang>` della lingua della pagina; `<link rel="canonical">` assoluto sul dominio richiesto e sul percorso della lingua; `<link rel="alternate" hreflang>` per **tutte** le versioni pubblicate (compresa la corrente) più `x-default` verso la lingua predefinita se esiste; selettore di lingua (`<nav class="lingue">`) con le sole traduzioni esistenti e pubblicate. Con una sola versione pubblicata non compaiono né `hreflang` né selettore. Le anteprime non emettono canonical né alternate (sono `noindex`).

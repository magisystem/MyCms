# ADR-0013 — Tabler e Tabler Icons per l'interfaccia di amministrazione

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

L'admin è server-rendered con Razor Pages (ADR-0002). Serve un tema grafico completo, senza introdurre una SPA né una pipeline Node obbligatoria. Nella cartella `admin-theme/` del repository sono presenti i pacchetti Tabler scelti dal committente.

## Decisione

- L'interfaccia admin usa **Tabler** (cartella `admin-theme/tabler`, versione 1.6.1, licenza MIT) come tema CSS/JS.
- Le icone del backend sono **Tabler Icons** (cartella `admin-theme/tabler-icons`, versione 3.49.0, licenza MIT).
- `admin-theme/` contiene i pacchetti scaricati, con le relative licenze, e resta nel repository come sorgente di riferimento. Non viene servita direttamente.
- Al bootstrap le cartelle **`tabler` e `tabler-icons` vengono copiate per intero** in `src/DvCms.Web/wwwroot/lib/` (rispettivamente `lib/tabler` e `lib/tabler-icons`), senza selezionare i soli file usati. Le pagine HTML demo di Tabler sono modelli da tradurre in layout e partial Razor.
- Le pagine HTML demo copiate in `wwwroot` non devono essere servite al pubblico: il middleware `MiddlewareRisorseStatiche` serve sotto `/lib/` solo estensioni di un elenco consentito (CSS, JS, font, immagini, SVG, mappe) e risponde 404 a tutto il resto (HTML, `.url`, PDF, ecc.), anche se il file esiste su disco. Lo stesso vale per i file di documentazione, sorgenti e mappe non necessari. Gli asset sono versionati, senza CDN esterni a runtime.
- Gli altri pacchetti (`tabler-emails`, `tabler-illustrations`, `tabler-avatars`) **non vengono copiati in `wwwroot`**. Le relative licenze sono possedute dal committente e il repository è privato; restano in `admin-theme/` per uso futuro (ad esempio template email). Le licenze sono personali (singolo licenziatario): il repository deve restare privato e i file non vanno ridistribuiti.
- Le librerie di `dist/libs` (ApexCharts, Tom Select, ecc.) sono incluse nella copia, ma si usano solo quando una schermata le richiede, dopo aver verificato la licenza di ciascuna (AGENTS.md, regola 11).
- Il tema del sito pubblico (temi tenant, ADR-0006) è indipendente da Tabler.

## Alternative considerate

- Admin custom senza framework: più lavoro di UI senza vantaggio.
- Altri template (AdminLTE, ecc.): non richiesti.
- SPA con componenti React/Angular: esclusa da ADR-0002.

## Conseguenze

- Nessuna dipendenza Node in produzione: `dist/` è già compilato.
- Le versioni di Tabler e Tabler Icons sono fissate nel repository; l'aggiornamento è una sostituzione deliberata della cartella con revisione.
- La copia intera aumenta dimensione di repository e di pubblicazione (centinaia di MB, in gran parte demo e icone in formati non usati). I duplicati con `admin-theme/` sono accettati dal committente, che li ripulirà in seguito; la pulizia, se tocca ciò che l'app serve, richiede un nuovo ADR.
- Accessibilità e mobile vanno verificati sulle schermate costruite, non assunti dal template.

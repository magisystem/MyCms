# ADR-0013 — Tabler e Tabler Icons per l'interfaccia di amministrazione

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

L'admin è server-rendered con Razor Pages (ADR-0002). Serve un tema grafico completo, senza introdurre una SPA né una pipeline Node obbligatoria. Nella cartella `admin-theme/` del repository sono presenti i pacchetti Tabler scelti dal committente.

## Decisione

- L'interfaccia admin usa **Tabler** (cartella `admin-theme/tabler`, versione 1.6.1, licenza MIT) come tema CSS/JS.
- Le icone del backend sono **Tabler Icons** (cartella `admin-theme/tabler-icons`, versione 3.49.0, licenza MIT).
- `admin-theme/` è la sorgente di riferimento (kit con pagine HTML demo), non una cartella servita. Le pagine demo sono modelli da tradurre in layout e partial Razor.
- Al bootstrap si copiano in `wwwroot` solo gli asset realmente usati (`dist/css`, `dist/js` e le singole librerie di `dist/libs` necessarie, più il webfont o gli SVG delle icone). Gli asset sono versionati, senza CDN esterni a runtime.
- Le librerie di `dist/libs` (ApexCharts, Tom Select, ecc.) si abilitano una alla volta quando una schermata le richiede, dopo aver verificato la licenza di ciascuna (AGENTS.md, regola 11).
- Gli altri pacchetti presenti in `admin-theme/` (`tabler-emails`, `tabler-illustrations`, `tabler-avatars`) **non sono approvati**: `tabler-emails` e `tabler-illustrations` hanno una licenza personale a pagamento; per `tabler-avatars` non c'è licenza nella cartella. Non vanno copiati in `wwwroot`, usati nell'app o ridistribuiti finché la licenza non è verificata.
- Il tema del sito pubblico (temi tenant, ADR-0006) è indipendente da Tabler.

## Alternative considerate

- Admin custom senza framework: più lavoro di UI senza vantaggio.
- Altri template (AdminLTE, ecc.): non richiesti.
- SPA con componenti React/Angular: esclusa da ADR-0002.

## Conseguenze

- Nessuna dipendenza Node in produzione: `dist/` è già compilato.
- Le versioni di Tabler e Tabler Icons sono fissate nel repository; l'aggiornamento è una sostituzione deliberata della cartella con revisione.
- Il repository contiene molto materiale non usato (centinaia di MB). Va deciso se tenerlo (vedi `docs/open-questions.md`).
- Accessibilità e mobile vanno verificati sulle schermate costruite, non assunti dal template.

# Decisioni da confermare (elenco consolidato)

Data: 2026-10-11. Raccoglie tutto ciò che è stato rimandato "alla fine" perché non bloccava il lavoro (`docs/open-questions.md`, rischi accettati in `docs/operations/revisione-sicurezza.md`, obiettivi in `docs/operations/backup-e-ripristino.md`, decisioni di esercizio emerse in M6) e le decisioni che **bloccano M7**. Per ogni voce c'è una proposta predefinita: se va bene basta dire "ok" e la si registra (con un ADR dove cambia un'architettura).

Stato di avanzamento: M0–M6 completate (M5 page builder con exit criteria soddisfatti, M6 con sonde, metriche, baseline); M7 e-commerce implementato (le decisioni A sono tutte prese); M8 (pilot) dipende dalle voci B, approvate con le proposte predefinite e da eseguire sull'ambiente reale.

**Approvazioni del committente del 2026-10-11:** A5 (resi e rimborsi), A7 (dati personali), macchina a stati dell'ordine, ADR-0011 (amministratori come acquirenti) e tutte le voci delle sezioni B e C con le proposte predefinite indicate.

## A. Bloccanti per M7 (e-commerce): `docs/architecture/ecommerce.md`, ADR-0009, ADR-0020

**Tutte decise il 2026-10-11** ([ADR-0020](decisions/ADR-0020-commerce-policies-and-paypal.md)): A1 **PayPal**, A2+A3 **solo EUR, IVA inclusa**, A4 **tariffa fissa per zona + ritiro**, A6 **prenotazione con scadenza 15 minuti**. **Restano da decidere A5 (resi e rimborsi) e A7 (dati personali)**: bloccano solo l'abilitazione del checkout di produzione, non lo sviluppo.

Senza queste non si implementa il checkout di produzione (regola del progetto). Si può invece iniziare subito la parte che non dipende da esse (catalogo e varianti con traduzioni, registrazione degli acquirenti, carrello e totali lato server).

| # | Decisione | Proposta predefinita | Alternative / note |
|---|---|---|---|
| A1 | **Provider di pagamento** e modalità di checkout | Pagina di pagamento ospitata dal provider (nessun dato di carta nel nostro sistema), con webhook firmati e idempotenti. Provider consigliato: Stripe Checkout (copre carte, Apple/Google Pay, SEPA; API HTTP, nessun SDK obbligatorio) | PayPal; provider italiani (Nexi, Satispay) se i clienti li richiedono. Un adapter per provider dietro un contratto unico, come per le email (ADR-0015): si parte da uno |
| A2 | **Mercati e valute** | Solo EUR, vendita in Italia/UE; importo in `decimal` con valuta esplicita già nel modello, così altre valute si aggiungono dopo | Multi-valuta fin dall'inizio (più lavoro su arrotondamenti e cambio) |
| A3 | **IVA e fatturazione** | Prezzi IVA inclusa, aliquota per prodotto (22% predefinita); il sistema calcola e conserva l'imponibile e l'IVA nello snapshot dell'ordine; **la fattura/ricevuta fiscale resta fuori dal sistema** (responsabilità del tenant, con esportazione degli ordini) | Prezzi IVA esclusa (B2B); integrazione con un software di fatturazione/SDI (fase successiva) |
| A4 | **Spedizioni** | Tariffa fissa per zona (Italia, UE) più ritiro in sede, definite per sito; nessuna integrazione con i corrieri | Tariffe a peso/valore; solo prodotti digitali (nessuna spedizione); integrazione con un corriere |
| A5 | **Resi e rimborsi** | Rimborso totale o parziale dal pannello del tenant, eseguito tramite il provider (transizioni di stato esplicite e idempotenti); testo della politica di reso e recesso (14 giorni UE) a cura del tenant | Resi gestiti interamente fuori dal sistema |
| A6 | **Inventario** | Prenotazione della giacenza all'avvio del checkout con scadenza (15 minuti), rilascio su scadenza/fallimento/annullamento, conferma al pagamento; riconciliazione periodica con il provider | Decremento solo al pagamento (rischio di vendere più del disponibile) |
| A7 | **Dati personali degli acquirenti** | Account obbligatorio per ordinare (già deciso, ADR-0011); ordini conservati 10 anni (obblighi fiscali), dati dell'account cancellabili/anonimizzabili su richiesta mantenendo gli snapshot degli ordini; informativa privacy e cookie a cura del tenant | Valutazione legale (GDPR) prima del pilot |

## B. Da confermare prima del pilot (M8) o della produzione

**Approvate il 2026-10-11 con le proposte predefinite**: restano da eseguire/verificare sull'ambiente reale.

| # | Voce | Proposta predefinita | Dove |
|---|---|---|---|
| B1 | **RPO/RTO e retention dei backup** | RPO 15 min database / 24 h file; RTO 4 h; retention 35 giorni | `backup-e-ripristino.md` |
| B2 | **Prima prova di ripristino su un ambiente reale** (il test automatico c'è, la prova con dati e infrastruttura reali no) | Da fare appena c'è l'ambiente di pre-produzione, seguendo il runbook; registrare tempi e problemi | `backup-e-ripristino.md` |
| B3 | **Rischi accettati** della revisione di sicurezza (6): blocco account di 5 minuti usabile per negare l'accesso (anche al `PlatformAdmin`, che non ha recupero password); token nell'indirizzo dei link di recupero/invito; `style-src 'unsafe-inline'`; nessuna scansione antimalware dei file; nessuna quota per tenant; host header nei link di recupero | Accettarli tutti per il pilot; rivedere `unsafe-inline` (ora l'editor non inietta CSS, ma i temi usano l'attributo `style` per le variabili) e la scansione antimalware prima di aprire altri formati di file | `revisione-sicurezza.md` |
| B3b | **SQL Server: isolamento `READ_COMMITTED_SNAPSHOT`** | Raccomandato per l'ambiente reale: i lettori non bloccano gli scrittori e spariscono quasi tutti i deadlock fra lettura e scrittura (emersi nei test di concorrenza di ordini e pagamenti). Intanto le operazioni idempotenti su ordine, pagamento e giacenza si ripetono automaticamente se SQL Server sceglie una vittima (errore 1205). L'opzione si imposta sul database, non nell'applicazione | `ecommerce.md` |
| B4 | **Hosting, regione e residenza dei dati**; edition di SQL Server; object storage e CDN | UE; SQL Server gestito o in contenitore con backup del log; archivio su file system con replica, object storage solo se serve CDN | `open-questions.md` |
| B5 | **Backend delle metriche e degli alert** | OpenTelemetry verso il sistema di monitoraggio già in uso (i meter ci sono: `DvCms.Web`, `DvCms.Cache`, `DvCms.Outbox`); alert suggeriti in `osservabilita.md`; sonde `/health/*` solo sulla rete interna | `osservabilita.md` |
| B6 | **Soglie di prestazioni** (es. p95 della pagina pubblica) e requisiti di carico/disponibilità | Da fissare con il primo cliente e misurare in pre-produzione con la baseline (`tools/baseline-prestazioni.mjs`) | `prestazioni.md` |
| B7 | **Email dei tenant**: dominio e mittente con SPF/DKIM/DMARC | Attività manuale del `PlatformAdmin` per ogni tenant, già documentata | `operations.md` |
| B8 | **Reset del `PlatformAdmin`** (oggi manuale dell'operatore; esiste il comando di reset della 2FA) | Mantenere la procedura manuale documentata per il pilot | `open-questions.md` |
| B9 | **Policy password** (lunghezza massima e complessità) | Minimo 12 caratteri (già applicato), massimo 128, nessuna regola di complessità (indicazioni NIST) | `open-questions.md` |
| B10 | **SSO/OIDC** per clienti enterprise | Fuori dal primo rilascio: solo account locali con 2FA | `open-questions.md` |

## C. Rimandate, non bloccanti (si decidono quando serve)

**Approvate il 2026-10-11 come indicate** (le proposte predefinite valgono finché non servono).

- **Page builder:** `backbone-undo` (dipendenza transitiva di GrapesJS, non più mantenuta, senza vulnerabilità note: rivalutare a ogni aggiornamento di GrapesJS); migrazione dei documenti fra versioni di template e di schema (oggi si cambia versione solo se il documento è già valido per la nuova); testo alternativo delle immagini per lingua collegato alla libreria media (ADR-0018); estensioni del testo formattato (titoli interni, tabelle, immagini in linea) con nuovi vettori di prova o con componenti dedicati.
- **Accessibilità:** prova con screen reader reali e audit WCAG automatico dell'editor (nuova dipendenza da valutare con la regola 11), contrasti dell'interfaccia dell'editor, spostamento da tastiera di un componente in un altro contenitore; requisiti di accessibilità dettagliati e design system.
- **Temi:** strategia e pipeline di rilascio dei pacchetti tema; compatibilità dei contenuti quando un tema si aggiorna; temi nello stesso processo o in deployment distinti.
- **E-commerce (M7.5):** approvare la macchina a stati dell'ordine (`ecommerce.md`: nessun annullamento dopo il pagamento, solo rimborso; nessuno stato "in preparazione"); se gli amministratori possano ordinare anche dall'area cliente (ADR-0011: il servizio lo ammette, l'area cliente oggi solo per gli `Utente`); elenco dei paesi con nomi al posto del codice a due lettere nella cassa; telefono del destinatario per i corrieri.
- **E-commerce (M7.7):** per accendere i pagamenti reali servono A5 e A7 e poi `Negozio:CheckoutProduzione=true`; da decidere anche il testo definitivo e le traduzioni dell'email di conferma ordine (oggi in italiano, con ripiego sulla lingua predefinita per le chiavi non tradotte) e se un ordine pagato ma da rimborsare debba avvisare il tenant per email (l'avviso di nuovo ordine e la riconciliazione con PayPal sono fatti: M7.17 e M7.21).
- **E-commerce (M7.8):** i componenti `negozio.*` del page builder, le immagini, le categorie e la ricerca sono fatti (M7.13–M7.20); vetrina sotto `/negozio` e slug riservato `negozio`: se un tenant volesse un altro percorso o una pagina CMS con quel nome, va deciso.
- **Email Microsoft 365 e Google Mail (A-010, ADR-0021):** deciso dal committente il 2026-10-12: consenso OAuth con app **del tenant**, configurata solo dal suo `TenantAdmin` (il `PlatformAdmin` non la vede). Resta aperto: la prova con caselle reali di Microsoft 365 e Google (le prove automatiche usano provider finti), la pubblicazione/verifica dell'app Google di ogni tenant (in stato *Testing* il refresh token scade dopo 7 giorni).
- **Pagamenti con Nexi (M7.25, ADR-0022):** da verificare con il Sandbox Nexi (servono le credenziali del terminale del committente): dominio della pagina ospitata, forma delle operazioni con incasso implicito, arrivo delle notifiche. Aperto: riconoscere i rimborsi fatti a mano dai pannelli PayPal/Nexi e segnare l'ordine come rimborsato (oggi sono ignorati).
- **Prodotto:** piani commerciali, limiti e quote per tenant, billing SaaS; visibilità limitata per il `Guest`.
- **Pulizia:** chiavi di testo statico non più usate; duplicati fra `admin-theme/` e `wwwroot` (li ripulisce il committente); HTMX solo se una schermata lo richiede.
- **Integrazione continua:** gli smoke test con browser reale (`frontend/page-builder/smoke/*.mjs`) non girano in CI (servono un'istanza, un database popolato e Chromium); si può valutare un job dedicato quando conviene il costo.

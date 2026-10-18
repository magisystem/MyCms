# ADR-0027 — Moduli di contatto personalizzabili dal TenantAdmin

- **Stato:** Proposed (da rivedere con il committente: nulla di questo è implementato)
- **Data:** 2026-10-18
- **Origine:** richiesta del committente del 2026-10-18: i moduli di contatto saranno **custom**, definiti e gestiti dal `TenantAdmin` di ogni sito, e **quando sono pubblici vanno protetti con ALTCHA** ([ADR-0026](ADR-0026-altcha.md)).
- **Modifica:** nessun ADR superato. Si appoggia su [ADR-0007](ADR-0007-grapesjs-page-builder.md) (componenti del page builder), [ADR-0010](ADR-0010-outbox-worker.md) (outbox), [ADR-0025](ADR-0025-modelli-email.md) (modelli email) e [ADR-0026](ADR-0026-altcha.md).

## Contesto

Oggi il prodotto non ha un modulo di contatto: le uniche form pubbliche sono registrazione degli acquirenti, recupero password e accesso. Un sito pubblicato però ne ha quasi sempre bisogno, e non esiste una forma «giusta» valida per tutti: ogni sito vuole campi, testi, destinatari e informativa propri. Il committente ha deciso che **non si fa un modulo fisso**: chi gestisce il sito pubblicato lo costruisce.

Vincoli che arrivano dal resto del prodotto:

- **Contenuti = dati, mai codice** (regola 7 di `AGENTS.md`): il tenant sceglie fra tipi di campo e regole predefiniti; nessun HTML, JavaScript, espressione regolare libera o Razor scritti dal tenant.
- **Isolamento fra tenant** (regola 5): ogni lettura e scrittura parte dal contesto del tenant dell'host; servono test di isolamento (regola 6).
- **Dati personali** di persone che non sono né i nostri utenti né acquirenti (visitatori che scrivono): servono informativa, minimizzazione, conservazione limitata e cancellazione su richiesta, come per gli acquirenti (A7, [`ecommerce.md`](../architecture/ecommerce.md)).
- **Abuso:** una form pubblica che manda email è un bersaglio di spam e di riempimento del database.

## Proposta

### 1. Il modulo è un'entità del tenant, non un pezzo della pagina

`Modulo` appartiene a un sito (`IdTenant`, `IdSito`), ha un nome interno, uno stato (`Bozza`, `Attivo`, `Archiviato`), i campi, i testi (titolo opzionale, testo del pulsante, messaggio di conferma, link all'informativa privacy) e le impostazioni di notifica e conservazione. Una pagina lo include con un nuovo componente del page builder, **`contenuto.modulo`**, che ha una sola proprietà obbligatoria `idModulo` (nuovo tipo di proprietà `IdModulo`, scelto nell'editor da un elenco dei moduli del sito: stesso schema di `negozio.scheda-prodotto` e `idProdotto`). Il server riconfronta al salvataggio che il modulo esista nel tenant e nel sito.

Perché entità separata e non campi dentro il documento della pagina: lo stesso modulo si riusa in più pagine e lingue; le richieste ricevute restano legate a un modulo anche se la pagina cambia o si ritira; le modifiche ai campi non sfruttano le revisioni della pagina (che servono a un altro scopo).

### 2. Campi: tipi predefiniti, regole predefinite

Tipi della prima versione: testo breve, testo lungo, email, telefono, numero, data, scelta singola (elenco o pulsanti), scelta multipla, **consenso** (casella con testo e link; obbligatoria se il modulo la prevede). Per ogni campo: etichetta, aiuto, obbligatorio sì/no, lunghezza massima, e per le scelte l'elenco dei valori. Niente espressioni regolari libere (rischio ReDoS e di errori silenziosi): le validazioni sono quelle del tipo (email valida, telefono con cifre e simboli ammessi, numero e data nei limiti). Limiti di sicurezza fissati dal prodotto: al massimo 20 campi per modulo, lunghezza totale dell'invio limitata, un `Modulo` per sito non supera un numero configurabile di campi e invii. **Nessun caricamento di file nella prima versione** (rischi di sicurezza e di spazio: eventuale ADR a parte).

Le etichette sono traducibili per lingua del sito (obbligatoria quella predefinita, le altre ripiegano su di essa).

### 3. Invio pubblico e protezione

Il modulo pubblicato manda i dati a un endpoint dedicato sulla stessa origine, `POST /moduli/{idModulo}` (non alla pagina che lo contiene), con token antiforgery come le altre form pubbliche. L'ordine dei controlli, tutti lato server:

1. il tenant dell'host e il modulo (esiste, è `Attivo`, è di quel sito) — altrimenti 404;
2. **prova ALTCHA** con `IVerificaAntiAbuso` (ADR-0026): senza prova valida non si salva nulla, non parte nessuna email;
3. limite di richieste per indirizzo (politica dedicata) e **tetto giornaliero per modulo** (protegge casella e database anche da un attacco distribuito);
4. validazione dei campi contro la **definizione attuale**, ignorando ogni campo non previsto (nessun over-posting);
5. salvataggio dell'invio e accodamento delle email nella stessa transazione (outbox, ADR-0010);
6. risposta: messaggio di conferma o reindirizzamento a una pagina del sito (a scelta del tenant), sempre con post/redirect/get.

**La protezione è obbligatoria, non un'opzione del singolo modulo.** Se in un'installazione ALTCHA è spento (`Altcha:Abilitato=false`) i moduli **non accettano invii** fuori da `Development` e dai test, e la pagina di gestione lo segnala: un modulo pubblico non deve poter funzionare senza protezione per una dimenticanza di configurazione. Un honeypot e un tempo minimo di compilazione si possono aggiungere come difese ulteriori; il widget ha già un ritardo minimo.

### 4. Cosa si conserva

`InvioModulo`: tenant, sito, modulo, lingua, data, stato (`Nuovo`, `Letto`, `Archiviato`) e i **valori inviati insieme a un'istantanea della definizione** (etichette e tipi al momento dell'invio), come lo snapshot degli ordini: se i campi cambiano, le richieste vecchie restano leggibili. **Non si conserva l'indirizzo IP né l'User-Agent**: il limite di richieste usa l'indirizzo solo in memoria. I campi sono dati personali del visitatore, trattati dal tenant come titolare: la piattaforma fornisce gli strumenti (informativa, conservazione, cancellazione), non il testo legale.

Conservazione: **ogni modulo ha un periodo in giorni** (proposta: 365, minimo 30, massimo 1825); la manutenzione periodica (`IAttivitaManutenzione`, ADR-0024) cancella gli invii scaduti. Cancellazione manuale del singolo invio dal `TenantAdmin` (richiesta dell'interessato) con voce di audit senza il contenuto.

### 5. Notifiche

Alla ricezione parte, via outbox, un'email ai **destinatari del modulo** (uno o più indirizzi scelti dal `TenantAdmin`, al massimo 5, oppure «tutti i TenantAdmin del tenant»), composta con il modello `nuovo-invio-modulo` (ADR-0025: nome del modulo, elenco etichetta-valore, link alla richiesta nell'admin, lingua). L'email contiene i dati personali: viene accodata come riservata (cifrata a riposo anche nell'outbox). Il tenant può personalizzare quel modello. L'email al visitatore («abbiamo ricevuto la tua richiesta») è **disattivata di default** e, se attivata, usa un modello separato e il limite di richieste: altrimenti il modulo diventa un modo per far spedire email a terzi.

### 6. Gestione nell'admin (solo `TenantAdmin`)

Cartella `/admin/moduli` con: elenco dei moduli del sito (stato, numero di richieste nuove); creazione e modifica guidate (campi con ordine, tipo, regole; testi; destinatari; conservazione); **anteprima** della form; **casella delle richieste** con filtro per modulo e stato, dettaglio, segna come letto, archivia, elimina; **esportazione CSV** di un modulo (con voce di audit: formato, numero di righe, filtro; mai i dati). Ogni modifica a un modulo e ogni esportazione o cancellazione è nel registro di audit. Il `PlatformAdmin` non accede alle richieste (dati dei visitatori del tenant): proposta come per le impostazioni email; da confermare.

### 7. Fuori dalla prima versione

Caricamento di file; logica condizionale fra campi; invio a servizi esterni (webhook, CRM, newsletter con doppio consenso); più passi; statistiche; risposte dall'admin; salvataggio di bozze del visitatore.

## Decisioni da prendere

| # | Decisione | Proposta |
|---|---|---|
| D1 | Il modulo è un'entità riusabile con componente `contenuto.modulo`? | **Sì** (entità separata); alternativa: campi come componenti dentro la pagina |
| D2 | Elenco dei tipi di campo della v1 | Quelli del paragrafo 2, senza file né espressioni regolari |
| D3 | Conservazione predefinita delle richieste | 365 giorni per modulo (30–1825), cancellazione automatica |
| D4 | Si conserva l'IP? | **No** |
| D5 | Accesso alle richieste | Solo `TenantAdmin` (non `PlatformAdmin`) |
| D6 | ALTCHA spento ⇒ moduli non accettano invii (fuori da sviluppo/test) | **Sì** |
| D7 | Email di risposta automatica al visitatore | Disattivata di default, attivabile per modulo |
| D8 | Destinatari delle notifiche | Fino a 5 indirizzi oppure tutti i TenantAdmin |
| D9 | Limiti per modulo (campi, invii al giorno) | 20 campi; tetto giornaliero configurabile (predefinito 200 invii/giorno/modulo) |
| D10 | Etichette per lingua | Obbligatoria la predefinita, ripiego sulla predefinita |
| D11 | Dopo l'invio | Messaggio di conferma oppure reindirizzamento a una pagina del sito |
| D12 | Esportazione CSV | Sì, solo `TenantAdmin`, con audit |

## Conseguenze

- Nuove tabelle per tenant (`Moduli`, `CampiModulo` o definizione in JSON versionato, `InvioModulo`), con filtro globale, chiavi composte tenant-sito e migrazione; test di isolamento tenant per ciascuna.
- Nuovo tipo di proprietà `IdModulo` e componente `contenuto.modulo` (nuova versione del registro e del modello di pagina, come per `negozio.*`), con rendering Razor lato server e nuova proprietà nell'editor.
- Nuovo modello email di fabbrica (italiano e inglese) e nuove azioni di audit.
- Nuovo endpoint pubblico con limite di richieste dedicato e tetto giornaliero per modulo; si riusano `IVerificaAntiAbuso` e il widget già presenti.
- I dati dei visitatori sono personali: documento di esercizio e nota sulla conservazione da aggiungere a `docs/operations`.
- Il tenant è titolare del trattamento: l'informativa privacy resta un suo testo (link obbligatorio se c'è un consenso), come per il negozio.

## Piano per fette verticali (se approvato)

1. **Modello e gestione:** entità `Modulo` e campi, servizio, pagine `/admin/moduli` (elenco, creazione, modifica, anteprima), audit, test di isolamento.
2. **Componente e invio:** `contenuto.modulo` nel builder, rendering pubblico, endpoint di invio con ALTCHA, validazione contro la definizione, salvataggio con istantanea, limiti.
3. **Notifiche:** modelli email `nuovo-invio-modulo` (e risposta automatica opzionale), outbox riservato.
4. **Casella delle richieste:** elenco, dettaglio, stati, cancellazione, esportazione CSV, conservazione automatica nella manutenzione.
5. **Documentazione e verifica nel browser:** esercizio, privacy, prova con Chromium del percorso completo.

## Alternative considerate

- **Modulo di contatto fisso** (nome, email, messaggio): il più semplice, ma contro la richiesta di moduli custom.
- **Servizi esterni** (Formspree, Google Forms…): dati dei visitatori fuori dal prodotto, nessun isolamento, dipendenza di terzi nelle pagine pubbliche.
- **Campi come componenti dentro il documento della pagina:** un solo flusso di modifica, ma ogni cambio crea una revisione della pagina, gli invii non hanno un'entità a cui legarsi e lo stesso modulo non si riusa; si potrebbe riprendere in seguito per la sola presentazione.
- **Espressioni regolari scelte dal tenant:** flessibili ma pericolose (ReDoS) e fragili; i tipi predefiniti coprono i casi comuni.

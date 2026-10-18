# Moduli di contatto — esercizio e privacy

Riferimento: [ADR-0027](../decisions/ADR-0027-moduli-di-contatto.md). Pagine: `/admin/moduli` (elenco, creazione, modifica, anteprima) e `/admin/moduli/richieste/{id}` (casella delle richieste). Solo il `TenantAdmin` del sito; il `PlatformAdmin` non vi accede perché le richieste contengono dati di visitatori del tenant.

## Cosa può fare il TenantAdmin

- Creare moduli con fino a 20 campi scelti fra: testo breve, testo lungo, email, telefono, numero, data, scelta singola, scelta multipla, consenso. Le etichette si scrivono per lingua (obbligatoria la predefinita, le altre ripiegano su di essa). Nessuna espressione regolare, HTML o script libero.
- Pubblicare (stato **Pubblicato**), riportare in bozza o archiviare. Solo un modulo pubblicato accetta richieste.
- Inserire il modulo in una pagina con il componente **Modulo di contatto** del page builder (disponibile solo se esiste almeno un modulo non archiviato).
- Scegliere dopo l'invio: un messaggio di conferma oppure una pagina del sito.
- Scegliere chi riceve l'avviso (fino a 5 indirizzi, oppure tutti i `TenantAdmin` attivi, al massimo 10) e se il visitatore riceve una risposta automatica (disattivata di default; parte solo se il modulo ha un campo email compilato).
- Leggere le richieste, segnarle come lette/nuove/archiviate, eliminarle, esportarle in CSV (UTF-8 con BOM, separatore `;`, formule neutralizzate).

## Protezione e limiti

- Ogni invio richiede una prova ALTCHA valida e usata una volta sola ([ADR-0026](../decisions/ADR-0026-altcha.md)). **Con ALTCHA spento (`Altcha:Abilitato=false`) fuori dall'ambiente Development i moduli non accettano invii** (risposta 503) e `/admin/moduli` mostra un avviso: serve `Altcha:HmacSignatureSecret` in configurazione di ambiente.
- Tetto giornaliero per modulo: 200 invii nelle ultime 24 ore (`Moduli:InviiMassimiAlGiorno`, 1–100000); oltre si risponde 429. Protegge casella email e database.
- Il server riverifica tutto contro la definizione **corrente** del modulo: campi non previsti sono ignorati, valori fuori forma rifiutati, tetto di 20000 caratteri complessivi.
- Il modulo non conserva indirizzo IP né User-Agent.

## Dati personali e conservazione

- Una richiesta contiene i dati scritti dal visitatore (spesso nome, email, messaggio): è un dato personale di una persona che non è utente né acquirente. Il titolare del trattamento è il tenant; serve un'informativa. Se il modulo ha un campo **consenso**, l'indirizzo dell'informativa è obbligatorio.
- **Conservazione per modulo**: 365 giorni di default, da 30 a 1825. Passato il termine la manutenzione periodica cancella le richieste (a lotti di 500); nel registro di audit resta solo il numero (`ScadenzaInviiModulo`).
- **Cancellazione su richiesta di un visitatore**: aprire la richiesta in `/admin/moduli/richieste/{id}` ed «Elimina definitivamente» (audit `EliminazioneInvioModulo`, senza dati).
- Un modulo con richieste non si elimina: si archivia; le richieste scadono da sole (poi il modulo si può eliminare).
- Le email di avviso e di risposta contengono i dati e sono **riservate**: cifrate anche nell'outbox. Il testo dei modelli `nuovo-invio-modulo` e `risposta-invio-modulo` si personalizza in `/admin/email/modelli` ([modelli email](modelli-email.md)).
- Backup e ripristino: le richieste stanno nella tabella `InviiModulo` e seguono il backup del database; dopo un ripristino le richieste oltre la conservazione verranno cancellate dalla manutenzione.

## Controlli e sospetti

- **«Nessuna email di avviso»**: serve un canale email configurato per il tenant (`/admin/email`); senza, la richiesta si salva comunque e si legge nell'admin. Guardare `MessaggiOutbox` (tipo `InvioEmail`) e i tentativi.
- **Il modulo risponde 503**: ALTCHA spento fuori da Development.
- **Il modulo risponde 429**: tetto giornaliero raggiunto o limite di accessi per indirizzo.
- **Il modulo non compare in una pagina pubblicata**: il modulo è in bozza o archiviato (il componente non produce nulla) oppure la pagina non è stata ripubblicata.
- **Audit**: `CreazioneModulo`, `ModificaModulo`, `CambioStatoModulo`, `EliminazioneModulo`, `EsportazioneModulo` (formato, numero di righe, filtro), `EliminazioneInvioModulo`, `ScadenzaInviiModulo`.

## Per chi sviluppa

Definizione e impostazioni sono JSON versionato nelle colonne `DefinizioneJson`/`ImpostazioniJson` (DTO in `DvCms.DTO.Moduli`, validazione pura in `DvCms.Core.Moduli.ValidatoreModulo`, lettura degli invii in `ElaboratoreInvioModulo`). L'istantanea di una richiesta è autodescrittiva (identificativo, etichetta nella lingua dell'invio, tipo, valori). Servizi: `IServizioModuli` (TenantAdmin), `IServizioModuloPubblico` (sito), `IServizioRichiesteModulo` (casella). Endpoint pubblico `POST /moduli/{id}`; conferma `GET /moduli/{id}/inviato`.

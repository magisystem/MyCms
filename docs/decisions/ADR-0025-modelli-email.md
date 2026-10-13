# ADR-0025 — Modelli email nel database con Fluid (Liquid)

- **Stato:** Accepted (richiesta del committente del 2026-10-13; libreria Fluid autorizzata dal committente)
- **Data:** 2026-10-13
- **Modifica:** completa [ADR-0015](ADR-0015-email-delivery.md) (invio) con la composizione dei messaggi; applica la regola 7 di `AGENTS.md` (i contenuti salvati sono dati, mai codice eseguibile) ai testi delle email.

## Contesto

I testi delle email stavano nel codice, frase per frase, tradotti con il catalogo dei testi statici: il tenant non poteva cambiare una parola né il formato (solo testo semplice, nessun HTML). Il committente vuole un vero sistema di modelli, salvati nel database, modificabili dal `TenantAdmin`.

## Decisione

1. **Motore: Fluid** (`Fluid.Core` 2.31, licenza MIT; dipendenze `Parlot` e `TimeZoneConverter`, anch'esse MIT). Sintassi **Liquid**: valori `{{ nome }}`, condizioni `{% if %}`, cicli `{% for %}`, pochi filtri. **Razor resta vietato** per i modelli nel database (regola 7).
2. **Sandbox.** Il motore vede solo i valori passati: testi, numeri, sì/no, elenchi e dizionari di questi (qualunque altro oggetto è rifiutato prima di arrivare al modello, quindi nessun accesso a membri .NET). Nessuna inclusione di file, nessuna funzione. Le esecuzioni hanno un tetto di 20.000 passi (`MotoreModelliEmail.PassiMassimi`): un ciclo senza fine si ferma con errore.
3. **Codifica.** Nell'HTML ogni valore è codificato (`HtmlEncoder`); nel testo e nell'oggetto no. Per i testi su più righe nell'HTML: `{{ x | escape | newline_to_br | raw }}`.
4. **Tabella `ModelliEmail`** (per tenant): `Codice`, `Lingua`, `Oggetto`, `CorpoHtml`, `CorpoTesto`, indice univoco su (tenant, codice, lingua), filtro globale per tenant, token di concorrenza. HTML e alternativa testo sono entrambi obbligatori.
5. **Catalogo in codice** (`CatalogoModelliEmail`): i codici, le variabili di ciascuno e i **modelli di fabbrica** in italiano e inglese. Sono codice, non dati: seguono la versione del prodotto e non si cancellano.
6. **Ripiego a cascata** (`ComponitoreEmail`): modello del tenant nella lingua del destinatario → modello del tenant nella **lingua predefinita del tenant** (quella predefinita del suo sito più antico; senza siti, l'italiano) → modello di fabbrica nella lingua → modello di fabbrica **italiano**. Un modello del tenant che non si lascia più rendere non ferma l'invio: si ripiega su quello di fabbrica e si registra un avviso.
7. **Validazione al salvataggio** (`ValidatoreModelloEmail`), con messaggi per chi scrive: sintassi, lunghezze (oggetto 200, HTML 100.000, testo 50.000 caratteri), solo variabili del modello, nessun elemento attivo nell'HTML (`<script>`, `<iframe>`, `<object>`, `<embed>`, `<form>`, `<link>`, `<meta>`, `<base>`, `<style>`, `javascript:`, gestori `on…=`), oggetto reso non vuoto. Nei **modelli critici** (recupero codice → `{{ codice }}`; recupero link, conferma registrazione, invito → `{{ link }}`) la variabile obbligatoria deve comparire sia nell'HTML sia nel testo resi con i dati d'esempio: un link dentro un `{% if %}` falso non conta.
8. **Anteprima** con dati d'esempio, senza salvare. L'HTML si mostra solo in un `<iframe sandbox="">` (nessuno script, modulo o accesso all'origine): quel che scrive il tenant non agisce nell'admin.
9. **Pagine** `/admin/email/modelli` (elenco) e `/admin/email/modelli/{codice}?lingua=…` (modifica, anteprima, ripristino), solo `TenantAdmin` (politica della cartella `/Admin/Email` e riverifica nel servizio sul database a ogni operazione); il `PlatformAdmin` non vi accede. Salvataggio e ripristino sono nel registro di audit (`ModificaModelloEmail`, `RipristinoModelloEmail`) senza il contenuto del modello.
10. **Confini fra progetti** (ADR-0024). Le interfacce `IComponitoreEmail` (compone) e `IServizioModelliEmail` (gestione) stanno in `Contracts`; i progetti di servizi compongono con `IComponitoreEmail` senza conoscere `Emails`. Codici (`CodiciModelloEmail`) e dati di scambio stanno in `DTO`; l'entità in `Models`; motore, catalogo, compositore e servizio in `Emails`.

## Conseguenze

- Le email di recupero password, conferma registrazione, invito, ordine (confermato, ricevuto con bonifico o contrassegno, avviso agli amministratori), rimborso e prova partono dai modelli. Il testo di fabbrica italiano riprende le frasi di prima; in più c'è l'HTML.
- Le traduzioni di quelle frasi nel catalogo dei testi statici non si usano più per le email: la lingua del destinatario sceglie il modello. Le voci restano nel catalogo e si possono ripulire.
- Un tenant che scrive un modello in una lingua è responsabile del contenuto; la piattaforma garantisce che non possa eseguire codice né togliere il codice o il link dalle email critiche.
- Nuova migrazione `ModelliEmail`.

## Alternative scartate

- **Razor per i modelli nel database:** compila ed esegue C#; vietato dalla regola 7.
- **Scriban / Handlebars.Net:** valutate; Fluid è la libreria che il committente conosce e usa in altri progetti, con sandbox esplicita dei membri e tetto ai passi.
- **Modelli di fabbrica nel database (seed):** richiederebbero migrazioni a ogni cambio di testo e si potrebbero cancellare; restare nel codice li rende parte della versione del prodotto.
- **Un solo modello senza lingua:** non copre clienti in lingue diverse.

# ADR-0020 — Politiche commerciali iniziali e pagamenti con PayPal

- **Stato:** Accepted (decisioni del committente del 2026-10-11 per A1, A2, A3, A4, A6; A5 e A7 restano proposte)
- **Data:** 2026-10-11

## Contesto

ADR-0009 definisce il modulo commerce ma lascia aperti provider, mercati, imposte, spedizioni e inventario. Il committente ha deciso le politiche iniziali che sbloccano M7 (`docs/decisioni-da-confermare.md`, sezione A).

## Decisioni

1. **Provider di pagamento: PayPal** (A1). Si usa l'API REST *Orders v2*: il server crea l'ordine di pagamento (importo e valuta ricalcolati dal server), il cliente approva sulla pagina PayPal, il server **cattura** il pagamento. Nessun dato di carta o di conto passa da noi. Gli eventi (`PAYMENT.CAPTURE.COMPLETED`, `...DENIED`, `...REFUNDED`, ecc.) arrivano da **webhook** la cui firma si verifica con l'API di verifica di PayPal prima di qualunque effetto; ogni evento ha un identificativo e si **deduplica**; le chiamate che creano o catturano usano l'intestazione `PayPal-Request-Id` (idempotenza) derivata dall'ordine e dal tentativo. Si usa `HttpClient` diretto, senza SDK, come per gli adapter email (ADR-0015). Il provider sta dietro un contratto (`IProviderPagamento`): un secondo provider si aggiunge senza toccare il dominio.
2. **Credenziali per tenant.** Ogni tenant ha il proprio account commerciante PayPal: *client id*, *secret* e *webhook id* si configurano per sito dal `PlatformAdmin`, con la stessa gestione dei segreti dei canali email (cifrati a riposo con la chiave maestra, in sola scrittura nell'interfaccia, mai nei log). Ambienti *sandbox* e *live* distinti per configurazione. Questo è un dettaglio di progetto derivato dalla decisione 1: se si preferisse un unico account di piattaforma con *payee* per tenant (PayPal Commerce Platform) serve una revisione di questo ADR.
3. **Mercati e valuta** (A2): solo **EUR**, vendita in Italia/UE. Gli importi sono `decimal` con valuta esplicita (codice a tre lettere) già nel modello, così altre valute si aggiungono senza cambiare lo schema; per ora la validazione ammette solo `EUR`.
4. **IVA** (A3): prezzi **IVA inclusa**, aliquota per prodotto (22% predefinita, valori ammessi da elenco). Il sistema calcola e conserva nello snapshot dell'ordine, per riga e in totale, prezzo lordo, imponibile e IVA (scorporo con arrotondamento al centesimo per riga, regola documentata e testata). **Fattura o ricevuta fiscale fuori dal sistema**: responsabilità del tenant, che dispone dell'esportazione degli ordini.
5. **Spedizioni** (A4): **tariffa fissa per zona** (Italia, UE) più **ritiro in sede**, definite per sito; nessuna integrazione con i corrieri. La tariffa scelta e il suo importo entrano nello snapshot dell'ordine; IVA sulla spedizione con l'aliquota ordinaria.
6. **Inventario** (A6): **prenotazione con scadenza di 15 minuti** all'avvio del checkout, rilascio su scadenza, fallimento o annullamento, conferma alla cattura del pagamento. La prenotazione è atomica a livello di database (aggiornamento condizionato sulla giacenza disponibile, mai lettura-poi-scrittura) e un processo periodico riconcilia prenotazioni scadute e pagamenti catturati.

## Non ancora deciso (restano proposte)

- **A5 Resi e rimborsi:** proposta di rimborso totale o parziale dal pannello del tenant tramite il provider, con testo della politica di reso a cura del tenant.
- **A7 Dati personali degli acquirenti:** proposta di conservare gli ordini 10 anni e anonimizzare i dati dell'account su richiesta mantenendo gli snapshot; informativa privacy a cura del tenant; da validare con un parere legale.

Finché A5 e A7 non sono approvate non si abilita il checkout di produzione; il catalogo, il carrello, l'ordine con snapshot, la prenotazione e l'adapter PayPal (in *sandbox*) si sviluppano e si testano.

## Conseguenze

- Il modulo commerce si implementa per vertical slice (catalogo, acquirenti, carrello e totali, ordine con snapshot, inventario, adapter PayPal con webhook, componenti del builder), ciascuna con test di isolamento fra tenant.
- La macchina a stati dell'ordine (`docs/architecture/ecommerce.md`) si definisce con la slice dell'ordine e passa da approvazione.
- I test del provider usano un servizio HTTP finto che riproduce le risposte di PayPal, come per le email; nessuna chiamata di rete reale nei test automatici e nessuna credenziale nel repository.

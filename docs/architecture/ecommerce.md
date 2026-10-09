# Modulo e-commerce

## Confine

E-commerce è un modulo interno con modelli, servizi e transizioni di dominio propri. Il CMS e il page builder possono comporre viste commerciali, ma non determinano prezzi, disponibilità, imposte, autorizzazione o stato dell'ordine.

## Capacità previste

- Catalogo: prodotti, varianti, SKU, categorie/collezioni, immagini e attributi.
- Prezzi e valuta esplicita.
- Inventario e movimenti di magazzino.
- Carrello e checkout.
- Ordini e righe con snapshot dei valori applicati all'acquisto.
- Pagamenti, tentativi, rimborsi e webhook.
- Spedizioni/evasione, se previste dal business.
- Account cliente ed email transazionali.

## Regole di sicurezza e integrità

- Il client invia ID prodotto/variante e quantità, mai un totale da considerare attendibile.
- Ricalcolare lato server prezzo, sconti, disponibilità e totali.
- Usare `decimal` e valuta esplicita per importi.
- Conservare snapshot dell'ordine: il catalogo può cambiare in seguito.
- Verificare le firme dei webhook del provider e deduplicare gli eventi.
- Rendere idempotenti le transizioni e le operazioni che possono essere ritentate.
- Non conservare PAN/CVV o dati sensibili della carta; usare checkout/token del provider.
- Applicare isolamento tenant a catalogo, carrello, ordine, cliente, inventario e webhook.
- Evitare double-submit e race condition sulla giacenza, usando transazioni/concurrency control.

## Eventi e stati

Definire gli stati consentiti come transizioni esplicite (es. `PendingPayment`, `Paid`, `PaymentFailed`, `Cancelled`, `RefundPending`, `Refunded`, `FulfillmentPending`, `Shipped`). La macchina a stati definitiva è da approvare con i requisiti commerciali; non consentire mutazioni arbitrarie dello stato.

## Integrazione con il builder

I componenti `commerce.*` referenziano prodotto/collezione/query tipizzata, non copiano il catalogo nel documento. Il renderer chiama il modulo Store attraverso servizi applicativi. Un'anteprima senza dati reali deve essere chiaramente distinta dal checkout reale.

## Decisioni ancora aperte

- Provider pagamenti e modalità checkout.
- Mercati, valute, IVA/imposte e responsabilità di calcolo.
- Metodi e zone spedizione.
- Prenotazione e rilascio delle quantità.
- Politica resi/rimborsi.
- Guest checkout vs account obbligatorio.
- Gestione dati personali, conservazione e obblighi legali applicabili.

Non implementare checkout produzione finché le decisioni pertinenti non sono approvate e testate.

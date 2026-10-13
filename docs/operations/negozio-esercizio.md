# Runbook — Accendere i pagamenti reali del negozio

Decisioni: [ADR-0020](../decisions/ADR-0020-commerce-policies-and-paypal.md). Descrizione tecnica: [`ecommerce.md`](../architecture/ecommerce.md). Le politiche commerciali (rimborsi, dati personali, macchina a stati, amministratori come acquirenti) sono approvate; restano solo le operazioni qui sotto, che richiedono credenziali reali e quindi una persona.

Chi lo fa: un `PlatformAdmin` (credenziali e verifica) e il responsabile dell'installazione (configurazione di ambiente).

## 0. Prima di tutto: il database

- Raccomandato **`READ_COMMITTED_SNAPSHOT`** sul database di produzione (decisione B3b): riduce i blocchi fra lettori e scrittori nei flussi di pagamento. L'applicazione ritenta da sola in caso di deadlock, ma non è una scusa per non attivarlo.
- Backup e ripristino provati: [`backup-e-ripristino.md`](backup-e-ripristino.md).
- La chiave dei segreti (`Sicurezza:ChiaveSegreti`) è impostata e **salvata fuori dal database**: senza di lei le credenziali PayPal non si leggono più.

## 1. Preparare il sito (a cura del tenant)

1. Negozio attivo per il sito (dal `PlatformAdmin`, scheda del tenant).
2. Almeno un prodotto pubblicato con una variante attiva, almeno un metodo di spedizione attivo (anche solo il ritiro in sede).
3. Canale email del tenant configurato (conferme al cliente, avvisi di nuovo ordine, rimborsi).
4. Pagine **`privacy`** e **`resi`** scritte dal tenant e pubblicate (slug esatti): il piè di pagina le collega. Il sistema non fornisce testi legali.
5. Fatture o ricevute: restano fuori dal sistema (responsabilità del tenant, che dispone dell'esportazione CSV degli ordini da `/admin/negozio/ordini`: un file per ordine o per articolo, per periodo e stato; contiene dati personali, quindi da conservare e condividere con cautela).

## 2. Conto PayPal del tenant

Ogni tenant usa il **proprio** conto commerciante (ADR-0020, voce 2).

1. Nel portale sviluppatori PayPal creare un'app **Live** per il conto e annotare *client id* e *secret*.
2. Registrare un **webhook** sull'app con indirizzo `https://<dominio pubblico del sito>/webhooks/paypal` e almeno questi eventi: `CHECKOUT.ORDER.APPROVED`, `PAYMENT.CAPTURE.COMPLETED`, `PAYMENT.CAPTURE.DENIED`, `PAYMENT.CAPTURE.DECLINED`, `PAYMENT.CAPTURE.REFUNDED`, `PAYMENT.REFUND.COMPLETED`, `PAYMENT.REFUND.FAILED`, `PAYMENT.REFUND.CANCELLED`. Annotare l'**identificativo del webhook**.
3. Nell'amministrazione di piattaforma: tenant → **Pagamenti** → ambiente **Produzione**, client id, secret e identificativo del webhook. I segreti si cifrano e non si rivedono.
4. Prima provare tutto in ambiente **Sandbox** con un'app Sandbox e un webhook Sandbox (stessa procedura, ambiente «Sandbox»): il Sandbox funziona sempre, senza interruttore.

### 2 bis. Terminale Nexi XPay del tenant (opzionale, ADR-0022)

Un sito può accettare anche carte con **Nexi** (il cliente sceglie alla cassa). Ogni tenant usa il **proprio** terminale.

1. Dal contratto con Nexi (o dal loro back office) ottenere la **chiave API** del terminale (`X-API-KEY`); il terminale va configurato con incasso automatico o, in ogni caso, il negozio chiede l'incasso implicito a ogni pagamento.
2. Nell'amministrazione di piattaforma: tenant → **Pagamenti** → riquadro **Nexi XPay** → ambiente e chiave API. La chiave si cifra e non si rivede.
3. **Nessun webhook da registrare**: l'indirizzo delle notifiche (`https://<dominio del sito>/webhooks/nexi`) viene inviato a Nexi a ogni pagamento. Deve essere raggiungibile da Internet (HTTPS, nessun filtro che blocchi Nexi); Nexi non ripete le notifiche perse, ma il ritorno del cliente e la riconciliazione periodica recuperano gli incassi.
4. Prima provare in ambiente **Sandbox** con la chiave di prova e le carte di prova della documentazione Nexi (*Test environment*): una carta che va a buon fine e una che viene rifiutata. Verificare che l'ordine diventi «Pagato» sia al ritorno sia con la sola notifica (chiudere la scheda prima del ritorno).
5. **Rimborsi:** si fanno dal back office Nexi (e per PayPal, volendo, dal pannello PayPal), non dal nostro pannello per Nexi. Il rimborso fatto lì **aggiorna l'ordine da solo** quando arriva la notifica del provider (webhook di rimborso PayPal; notifica `REFUND` di Nexi): l'ordine mostra il rimborso con l'etichetta «Eseguito dal pannello del provider» e, a rimborso completo, diventa «Rimborsato». Se la notifica non arriva (sito non raggiungibile in quel momento), l'ordine resta com'era: per PayPal si può far reinviare il webhook dal portale sviluppatori, per Nexi la notifica non viene ripetuta e lo stato va corretto annotando il rimborso fuori dal sistema. Dopo un rimborso completo, se la merce rientra, usare «Rimetti in giacenza» dal dettaglio ordine.

La «Verifica di esercizio» considera pronto un sito con almeno un provider configurato.

### 2 ter. Bonifico e contrassegno (a cura del TenantAdmin, ADR-0023)

Non servono credenziali né webhook: il `TenantAdmin` li attiva da **Admin → Pagamenti** (`/admin/negozio/pagamenti`).

1. **Bonifico:** intestatario, IBAN (verificato), giorni per pagare (la merce è trattenuta fino ad allora, poi l'ordine si annulla da solo se ha articoli con giacenza gestita), istruzioni. Quando arriva un ordine il cliente riceve le coordinate e la causale `Ordine <numero>`: **controllare l'estratto conto** e, a ogni bonifico arrivato, aprire l'ordine e premere «Bonifico ricevuto: segna come pagato». Un bonifico arrivato dopo la scadenza non si registra su un ordine annullato: restituirlo o far rifare l'ordine.
2. **Contrassegno:** commissione fissa e/o percentuale (IVA inclusa, aliquota ordinaria), eventuale soglia oltre la quale è gratis e limiti di totale (usare il massimo assicurato dal corriere). Si offre solo per le consegne in Italia a domicilio. Gli ordini arrivano «Da spedire (pagamento alla consegna)»: spedirli con un corriere che offre il contrassegno e premere «Segna come spedito»; se la consegna non riesce premere «Consegna non riuscita» e, quando la merce rientra, rimetterla in giacenza dalla pagina delle giacenze. L'incasso e il rendiconto del corriere restano fuori dal sistema.
3. **Rimborsi:** per i pagamenti manuali non c'è il pulsante: si restituisce con un bonifico e il dettaglio ordine lo ricorda.
4. La «Verifica di esercizio» considera pronto un sito con almeno un metodo di pagamento tra provider online, bonifico (con intestatario e IBAN) e contrassegno; il vincolo dell'interruttore di produzione (sotto) riguarda solo i provider online.

## 3. Interruttore di produzione

Con ambiente Produzione il pagamento non parte finché la configurazione dell'installazione non ha `Negozio:CheckoutProduzione=true` (predefinito falso). Impostarlo come variabile d'ambiente `Negozio__CheckoutProduzione=true` e riavviare. Altre opzioni utili:

| Configurazione | Predefinito | Significato |
|---|---|---|
| `Negozio:CheckoutProduzione` | `false` | Consente i pagamenti in ambiente Produzione |
| `Negozio:AvvisoNuovoOrdineAmministratori` | `true` | Email agli amministratori del tenant a ogni ordine pagato |
| `Negozio:ConservazioneOrdiniAnni` | `10` | Dopo quanti anni si cancellano i dati personali negli ordini |
| `Manutenzione:Abilitata` | `true` | Scadenza prenotazioni, riconciliazione PayPal, conservazione dati: **non spegnere** in produzione |

## 4. Verifica di esercizio

Nella stessa pagina **Pagamenti** del tenant, sotto il modulo, la **Verifica di esercizio** elenca i controlli (negozio attivo, credenziali e webhook, interruttore, prodotti, spedizioni, email, amministratori, pagine legali, manutenzione, dominio pubblico) e dice se il sito è «Pronto per i pagamenti». I controlli *bloccanti* vanno tutti a posto; quelli *da controllare* sono consigliati. La verifica è in sola lettura e non mostra segreti.

## 5. Prova con un ordine reale

1. Dall'area di amministrazione, «Acquista come cliente» (oppure un account acquirente) e ordinare l'articolo meno caro, con ritiro in sede se possibile.
2. Pagare con un conto PayPal vero. Controllare: ordine **Pagato** nel pannello, email di conferma al cliente, email di avviso agli amministratori, movimento di vendita in giacenza (se gestita).
3. **Rimborsare** l'ordine per intero dal pannello (dettaglio ordine → Rimborso). Controllare: stato **Rimborsato**, email di rimborso, importo restituito sul conto PayPal di prova. Se la merce è rientrata, «Rimetti in giacenza».
4. Controllare nel conto PayPal che il webhook abbia consegnato gli eventi (stato 200). Un evento rifiutato per firma o dominio sbagliato si vede lì.

## 6. In esercizio

- **Incasso senza ordine pagato** (ritorno del cliente perso, ordine scaduto): la riconciliazione oraria lo recupera; se l'ordine nel frattempo era chiuso, l'ordine mostra «Il provider ha incassato…» e il pulsante «Rimborsa l'incasso».
- **Rimborso «in corso»** oltre qualche ora: pulsante «Verifica l'esito» nel dettaglio dell'ordine.
- **Richiesta di cancellazione dei dati** di un cliente: l'interessato usa «I miei dati» nell'area cliente oppure l'amministratore usa «Anonimizza l'account dell'acquirente» dal dettaglio di un suo ordine. Gli ordini pagati si conservano 10 anni.
- **Cambio di credenziali PayPal:** stessa pagina dei pagamenti; lasciare vuoto un segreto lo lascia invariato.
- **Spegnere i pagamenti in emergenza:** togliere `Negozio__CheckoutProduzione` (o metterlo a `false`) e riavviare; il Sandbox continua a funzionare. Gli ordini già pagati non sono toccati.

## Cosa non c'è ancora

Rimborsi per singolo articolo, fatturazione, più valute, altri provider oltre a PayPal e Nexi (il contratto `IGatewayPagamento` li ammette), il recupero dei rimborsi fatti a mano mentre il sito non era raggiungibile (oggi servono la notifica o l'annotazione manuale).

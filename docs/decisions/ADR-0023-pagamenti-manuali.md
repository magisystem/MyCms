# ADR-0023 — Pagamenti manuali: bonifico bancario e contrassegno

- **Stato:** Accepted (richiesta del committente del 2026-10-13: «Bonifico bancario» e «Contrassegno», attivabili dal `TenantAdmin`, con i campi necessari per il contrassegno). Le scelte di dettaglio sotto indicate sono **proposte predefinite** in attesa di conferma (vedi «Da confermare»).
- **Data:** 2026-10-13
- **Modifica:** ADR-0020, voci 1 e 9 (provider di pagamento e macchina a stati dell'ordine): oltre ai provider online (PayPal, Nexi, ADR-0022) il negozio ammette due pagamenti **manuali**, che non passano da un provider e che il tenant conferma a mano.

## Contesto

Molti negozi italiani accettano il bonifico e il contrassegno. Sono pagamenti diversi da quelli online: non c'è un provider che dica se il denaro è arrivato, il contrassegno cambia il totale (commissione) e la merce va trattenuta per tempi diversi dai 15 minuti del pagamento online.

## Decisione

1. **Modalità di pagamento dell'ordine** (`ModalitaPagamento`: `Online`, `Bonifico`, `Contrassegno`, salvata sull'ordine e immutabile). Si sceglie **alla cassa**, perché cambia ordine, totale e prenotazione; `Online` è il comportamento di prima (il provider si sceglie poi sulla pagina dell'ordine) ed è il valore degli ordini esistenti e di chi non indica nulla. La cassa offre i pagamenti attivi del sito; l'online si offre se c'è un provider pronto o se il sito non ha pagamenti manuali.
2. **Impostazioni per sito, gestite dal `TenantAdmin`** (anche dal `PlatformAdmin` che entra nel tenant), pagina `/admin/negozio/pagamenti`, tabella `ImpostazioniPagamentoOfflineSiti`, una riga per sito, tutto spento di partenza. Nessun segreto: l'IBAN si comunica ai clienti. Ogni modifica è nell'audit (`ModificaPagamentiOffline`, solo gli stati attivo/spento, mai l'IBAN).
3. **Bonifico.** Campi: intestatario e IBAN (obbligatori se attivo; IBAN controllato nella struttura e nelle cifre di controllo, modulo 97, 27 caratteri per l'Italia), BIC e nome della banca (facoltativi), **giorni per pagare** (1–60, predefinito 7), istruzioni libere. L'ordine nasce `InAttesaDiPagamento` con la merce prenotata fino alla scadenza (non 15 minuti: i giorni impostati); passata la scadenza la manutenzione periodica lo annulla e libera la merce, come per l'online (vale per gli articoli con giacenza gestita: senza prenotazioni l'ordine non scade da solo e lo annulla il tenant). Il cliente riceve subito una email con importo, coordinate, **causale** (`Ordine <numero>`) e termine, e la stessa pagina dell'ordine; il tenant riceve l'avviso di nuovo ordine. Quando il bonifico arriva, il tenant preme **«Bonifico ricevuto: segna come pagato»**: l'ordine diventa `Pagato`, la merce prenotata esce dal magazzino e il cliente riceve la conferma di sempre; da lì l'evasione è quella di un ordine pagato. Il tenant può anche **annullare** l'ordine in attesa. Il sistema non legge l'estratto conto: la riconciliazione è a mano.
4. **Contrassegno.** Offerto **solo per la consegna a domicilio in Italia** (metodo a zona, area Italia): è un servizio dei corrieri nazionali e non ha senso per il ritiro in sede né per l'estero. L'ordine nasce nel nuovo stato **`DaSpedireContrassegno`** (accettato, da spedire; il pagamento avviene alla consegna e non passa da noi), con la merce prenotata **senza scadenza** fino alla spedizione o all'annullamento. Quando il tenant segna «spedito» l'ordine diventa `Spedito` e la merce esce dal magazzino; fino ad allora il cliente o il tenant possono annullarlo e la merce torna libera. Se la consegna non riesce (rifiutata, mai ritirata) il tenant preme **«Consegna non riuscita»**: `Spedito` → `Annullato` (transizione ammessa solo per il contrassegno); la merce rientrata si rimette in giacenza dalla pagina delle giacenze. L'incasso lo fa il corriere: **il sistema non lo traccia** (nessuno stato «incassato»).
5. **Commissione del contrassegno** (campi impostati dal tenant, tutti in EUR con due decimali, **IVA inclusa**): **parte fissa** (per ordine) **+ percentuale** (0–100, due decimali) del totale dell'ordine prima della commissione (articoli e spedizione); **gratuita dal totale** indicato (facoltativo); **totale minimo** e **massimo** per offrire il contrassegno (facoltativi, ad esempio il limite assicurato dal corriere). La commissione è un importo calcolato dal server, mai inviato dal cliente: entra nel totale visto alla cassa (che torna indietro solo come controllo di coerenza, per ogni combinazione consegna e pagamento) e nello snapshot dell'ordine (`CommissioneLorda`, `CommissioneImponibile`, `CommissioneIva`; vincoli del database: lordo = imponibile + IVA, totale ≥ spedizione + commissione, commissione diversa da zero solo per il contrassegno). **IVA: aliquota ordinaria (22%) scorporata come per la spedizione.** Altri sistemi di calcolo (scaglioni per importo, per peso, per zona o per corriere) non sono previsti: si aggiungono se servono.
6. **Nessun rimborso dal pannello** per i pagamenti manuali: non c'è un provider a cui chiederlo. I rimborsi si fanno a mano (bonifico di restituzione) e il dettaglio ordine lo indica; l'ordine non passa a `Rimborsato`. L'esportazione CSV riporta il pagamento e la commissione (e una riga «Commissione» nel formato per articolo, così le righe sommano al totale).
7. **Pagamento online negato** sugli ordini manuali (`AvviaAsync` → «ordine chiuso»): un ordine a bonifico o in contrassegno non apre mai un pagamento presso un provider.
8. **Interruttore di produzione:** `Negozio:CheckoutProduzione` riguarda solo i provider con denaro reale; i pagamenti manuali non ne dipendono. La verifica di esercizio li conta come metodo di pagamento pronto (bonifico con intestatario e IBAN; contrassegno attivo), segnala un bonifico attivo senza coordinate come bloccante e, senza provider online, non mostra né l'ambiente di prova né l'interruttore.

## Macchina a stati (integra ADR-0020, voce 9)

| Da | A | Chi / quando |
|---|---|---|
| `InAttesaDiPagamento` (bonifico) | `Pagato` | il tenant registra il bonifico ricevuto |
| `InAttesaDiPagamento` (bonifico) | `Annullato` | il tenant o il cliente annulla, o scade la prenotazione |
| `DaSpedireContrassegno` | `Spedito` | il tenant segna la spedizione (la merce esce dal magazzino) |
| `DaSpedireContrassegno` | `Annullato` | il cliente o il tenant annulla prima della spedizione |
| `Spedito` (contrassegno) | `Annullato` | consegna non riuscita |

`PagamentoFallito` esiste solo per i pagamenti online. Ogni passaggio resta un aggiornamento condizionale con audit (`PagamentoOfflineRicevuto`, `AnnullamentoOrdineAmministrazione`, `ConsegnaNonRiuscita`, `EvasioneOrdine`).

## Alternative considerate

- **Scegliere il pagamento sulla pagina dell'ordine** (come per PayPal/Nexi): impossibile per il contrassegno, che cambia il totale prima che l'ordine esista, e fuorviante per il bonifico (la merce si trattiene per giorni).
- **Stato «incassato» per il contrassegno** e riconciliazione con i corrieri: aggiunge stati e importazioni di rendiconti; rimandato.
- **Commissione a scaglioni, per peso o per zona:** non richiesti; parte fissa + percentuale con soglia di gratuità e limiti copre il caso tipico (importo fisso per spedizione) e quelli più diffusi.
- **Esenzione IVA o aliquota del bene principale per la commissione:** richiede una valutazione fiscale caso per caso; si applica l'aliquota ordinaria come per la spedizione (ADR-0020, voce 5).
- **Riconoscere i bonifici dalla banca (PSD2/CBI) in automatico:** richiede un aggregatore e contratti; fuori ambito.

## Da confermare

- **IVA della commissione al 22%** (come la spedizione): se il commercialista indica un trattamento diverso (ad esempio l'aliquota del bene principale per ordini con un'unica aliquota ridotta) va cambiato.
- **Contrassegno solo in Italia e solo a domicilio.**
- **Bonifico che scade dopo i giorni impostati e libera la merce** (e il fatto che, senza giacenza gestita, non scade da solo).
- **Nessun rimborso dal pannello e nessun tracciamento dell'incasso del contrassegno.**

## Conseguenze

- Migrazione `AggiungiPagamentiOffline`: tabella `ImpostazioniPagamentoOfflineSiti` e, su `Ordini`, `ModalitaPagamento` (predefinito `Online` per gli ordini esistenti), le tre colonne della commissione e i vincoli `CK_Ordini_Totali` (esteso) e `CK_Ordini_Commissione`. Il nuovo stato `DaSpedireContrassegno` è testo, nessuna conversione di dati.
- Test: regole pure (IBAN, commissione, preventivo, transizioni), servizio delle impostazioni, ordini e azioni di amministrazione, pagine web, verifica di esercizio e isolamento fra tenant.

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
- Account acquirente (`Utente`) ed email transazionali (adapter di ADR-0015).

## Chi può ordinare

- Gli ordini richiedono un account autenticato: `Utente` (acquirente registrato), `TenantAdmin` (solo nel proprio tenant) o `PlatformAdmin` (in qualunque tenant). Il `Guest` non può ordinare (nessun guest checkout, per ora).
- `TenantAdmin` e `PlatformAdmin` si autenticano sul sito pubblico con le stesse credenziali dell'admin e vi hanno solo capacità di acquirente.
- La registrazione degli `Utente` è abilitata per sito e solo se l'e-commerce è attivo; l'`Utente` può solo emettere e consultare i propri ordini e non accede all'admin.
- Gli account `Utente` appartengono a un solo tenant: la stessa email può registrarsi su tenant diversi come account distinti.

## Registrazione e accesso degli acquirenti (M7.2, F-000)

- **Abilitazione:** il `PlatformAdmin` attiva il negozio del sito (`Sito.CommercioAttivo`) e poi la registrazione degli acquirenti (`RegistrazioneAcquirentiAbilitata`, possibile solo con il negozio attivo). Senza, `/account/registrazione` e `/account/conferma` danno 404; `/account/accesso` e `/account` esistono con il negozio attivo.
- **Registrazione senza password:** si indicano email e nome. Il server crea un `Utente` del tenant dell'host con stato `InAttesaDiConferma` e una password che nessuno conosce, e invia (dall'outbox, cifrata a riposo) un link monouso valido 24 ore sul dominio registrato del tenant. **La password si sceglie dalla pagina del link**: chi registra l'indirizzo di un altro non può quindi prendere l'account. Fino alla conferma l'account non può accedere né recuperare la password.
- **Nessuna enumerazione:** la risposta è la stessa per un indirizzo nuovo, già in attesa o già registrato (anche come amministratore); per un indirizzo già attivo non parte alcuna email; per uno in attesa il link si rinvia al più 3 volte all'ora e solo l'ultimo vale. Se il canale email o la chiave dei segreti mancano, la registrazione risulta non disponibile e non lascia account in attesa.
- **Area cliente:** cookie `.DvCms.Cliente` (percorso `/`, legato all'host, distinto da quello dell'admin) e schema `DvCmsCliente`; vale solo per `Utente` (il validatore scarta ogni altro ruolo e ricontrolla a ogni richiesta account attivo, timbro di sicurezza e tenant dell'host). L'accesso cliente non cerca nemmeno gli account amministrativi (nessun effetto sui loro contatori, nessun audit di piattaforma). Richieste limitate come l'accesso admin; `/account/esci` richiede la sessione (il token antiforgery è legato all'utente).
- **Manutenzione:** gli account mai confermati si eliminano dopo 7 giorni (e, a cascata, i loro link), così l'indirizzo torna libero.
- **Recupero password:** la pagina di accesso cliente ha «Password dimenticata?». Il flusso (codice e collegamento in due email) è lo stesso dell'amministrazione, con l'aspetto dell'accesso admin, ma con la provenienza `da=account` (valore fisso, qualunque altro è ignorato): si parte dall'accesso cliente, il collegamento dell'email lo porta con sé e tutti i «torna all'accesso» e «vai all'accesso» puntano a `/account/accesso`.
- **Amministratori come acquirenti (ADR-0011):** un `TenantAdmin` del tenant o un `PlatformAdmin` usa la voce di menu «Acquista come cliente» (`POST /admin/negozio/acquista`): dalla sessione di amministrazione, già passata dalla verifica a due fattori, ottiene la sessione dell'area cliente. Non esiste un secondo accesso con la sola password. Sul sito pubblico il principal ha **sempre il ruolo `Utente`** (solo capacità di acquirente) e il cookie non vale nulla in `/admin`; la sessione cliente cade con la disabilitazione dell'account e con il cambio password (timbro di sicurezza) e non vale su un altro tenant. L'ordine registra l'account dell'amministratore; l'ingresso è nell'audit.
- **Ancora da fare:** pagine di recupero con l'aspetto dell'area cliente.

## Carrello e totali (M7.3, F-002)

- **Il cliente invia solo identificativi e quantità.** `IServizioCarrello` non ha alcun parametro per prezzi, aliquote o totali: li legge sempre dal catalogo attuale. Un prezzo, un'aliquota o un totale aggiunti al modulo si ignorano (test). Il carrello (`Carrelli`, `RigheCarrello`) non conserva importi.
- **Chi:** carrello personale, uno per account e per sito; ordina chi è autenticato e attivo (`Utente` o `TenantAdmin` del tenant dell'host, `PlatformAdmin` ovunque, ADR-0011). Il `Guest` non ha carrello (nessun guest checkout). Per ora il sito pubblico apre l'area cliente solo agli `Utente`.
- **Limiti:** 1–99 unità per riga, 50 righe diverse; oltre, `DatiNonValidi`. Si acquista solo una variante attiva di un prodotto pubblicato del sito dell'host: bozza, archiviata, disattivata o di un altro tenant danno `NonTrovato`.
- **Quantità disponibile:** ogni riga porta `Massimo` (con giacenza gestita, la parte non prenotata né venduta; altrimenti 99). Se la quantità del carrello lo supera il carrello avvisa («Disponibili solo N») e non offre il passaggio alla cassa; è un avviso, la garanzia resta la prenotazione alla creazione dell'ordine.
- **Disponibilità:** un articolo che perde disponibilità (prodotto non più pubblicato, variante disattivata) resta visibile come "non più disponibile", fuori dai totali, non si può aumentare e si può togliere. Una variante eliminata dal catalogo scompare anche dai carrelli (gli ordini hanno uno snapshot e non puntano alle varianti, quindi una variante già ordinata si può eliminare senza toccarli).
- **Totali** (`CalcolatoreTotali`, puro e con test sistematici): il lordo di riga è prezzo × quantità; l'imponibile di riga è il lordo diviso (1 + aliquota/100), arrotondato al centesimo (lontano dallo zero); l'IVA di riga è la differenza (lordo = imponibile + IVA per riga e nei totali); i totali sono somme delle righe, non un nuovo scorporo. Importi `decimal`, valuta EUR.
- **Pagina:** `/account/carrello` (mai in cache, azioni con antiforgery). Il pulsante "aggiungi" delle pagine prodotto arriverà con i componenti `negozio.*` del builder: userà `POST /account/carrello?handler=Aggiungi` con `idVariante` e `quantita`.
- Il metodo di consegna si sceglie nella cassa (sezione Ordini).

## Metodi di spedizione (M7.4, F-002, ADR-0020)

- **Cosa:** per sito, fino a 20 metodi (`MetodiSpedizione`): *consegna con tariffa fissa per area* (Italia, resto dell'Unione europea) oppure *ritiro in sede*. Ogni metodo ha nome (1–100 caratteri, senza caratteri di controllo), stato attivo/disattivo e posizione di presentazione. Nessun peso, scaglione o corriere in tempo reale (fuori ambito, ADR-0020).
- **Tariffa:** in EUR, **IVA inclusa**, da 0 a 10.000 con al massimo due decimali; zero è ammesso (spedizione gratuita). Il ritiro non ha area né costo: una tariffa o un'area inviate per il ritiro vengono scartate dal server (vincolo `CK_MetodiSpedizione_TipoEArea` anche nel database).
- **Totali:** la spedizione entra nei totali come una riga con **aliquota ordinaria** (22%, `CalcolatoreTotali.CalcolaSpedizione`): imponibile e IVA si scorporano come per gli articoli e `lordo = imponibile + IVA` resta vero nei totali. Un metodo a tariffa zero dà una riga a zero.
- **Aree:** `PaesiSpedizione` definisce l'appartenenza di un paese all'area (Italia = IT; resto UE = gli altri 26 Stati membri). L'indirizzo di consegna dell'ordine sarà validato contro l'area del metodo scelto nella slice dell'ordine; l'ordine conserverà uno snapshot del metodo e non punterà a questa riga, che si può modificare o eliminare.
- **Chi:** gestiscono `TenantAdmin` del tenant e `PlatformAdmin` (ruolo riverificato a ogni operazione) solo con il negozio attivo del sito; l'elenco dei metodi attivi lo vede ogni account che può ordinare. Pagina `/admin/negozio/spedizioni` (404 senza negozio attivo, voce di menu solo con negozio attivo, antiforgery, testi codificati); ogni modifica è nell'audit (`ModificaMetodoSpedizione`).

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

## Eventi e stati (approvata il 2026-10-11)

Gli stati sono un'enumerazione chiusa (`StatoOrdine`) e **ogni passaggio** passa da `TransizioniOrdine` (dominio): un passaggio non elencato non esiste. Lo stato non si modifica con un'assegnazione libera.

| Da | A | Chi / quando |
|---|---|---|
| `InAttesaDiPagamento` | `Pagato` | cattura PayPal riuscita (M7.7) |
| `InAttesaDiPagamento` | `PagamentoFallito` | pagamento rifiutato o scaduto (M7.7) |
| `InAttesaDiPagamento` | `Annullato` | il cliente annulla, o scade la prenotazione (M7.6, fatto) |
| `PagamentoFallito` | `InAttesaDiPagamento` | nuovo tentativo di pagamento (M7.7) |
| `PagamentoFallito` | `Annullato` | il cliente annulla, o scade la prenotazione |
| `Pagato` | `Spedito` | il tenant segna la spedizione (solo metodo a zona) |
| `Pagato` | `Ritirato` | il tenant segna il ritiro (solo metodo ritiro) |
| `Pagato`, `Spedito`, `Ritirato` | `Rimborsato` | i rimborsi completati coprono il totale (A5); i rimborsi parziali non cambiano lo stato |

`Annullato` e `Rimborsato` sono **finali**; `Spedito` e `Ritirato` possono ancora diventare `Rimborsato`. Un ordine pagato non si annulla: si rimborsa (sezione Rimborsi). Un tentativo di pagamento è un'entità a parte (`TentativoPagamento`, M7.7); l'ordine cambia stato solo in risposta a esiti verificati (cattura lato server o webhook firmato e deduplicato), mai su dichiarazione del browser. Ogni transizione è una scrittura con controllo di concorrenza (`TokenConcorrenza`) e finisce nell'audit.

**Approvata** dal committente il 2026-10-11 (A5: rimborsi). Ogni transizione è una scrittura condizionale e finisce nell'audit.

## Ordini (M7.5, F-003)

- **Snapshot:** `Ordini` e `RigheOrdine` conservano una copia di tutto ciò che conta al momento dell'acquisto — per riga codice, nome ed etichetta nella lingua del cliente, prezzo unitario lordo, aliquota, lordo, imponibile, IVA; per l'ordine metodo di spedizione (tipo, area, nome, importo scorporato), nome ed email dell'acquirente, indirizzo — e **non hanno chiavi verso varianti o metodi di spedizione**: catalogo e tariffe si possono modificare o eliminare senza toccare lo storico (test). La variante resta come semplice riferimento (`IdVariante`, senza vincolo) per la giacenza.
- **Totali:** comprendono la spedizione; il database impone `lordo = imponibile + IVA` per riga, per spedizione e per ordine, e la coerenza fra tipo di consegna, area e indirizzo (`CK_Ordini_*`, `CK_RigheOrdine_*`).
- **Numero d'ordine:** progressivo per sito, senza buchi né duplicati: si prende incrementando `Siti.UltimoNumeroOrdine` nella stessa transazione che crea l'ordine (indice unico `(IdSito, Numero)`).
- **Creazione** (`IServizioOrdini.CreaDaCarrelloAsync`, un'unica transazione): il cliente invia solo metodo di spedizione, indirizzo e il totale che ha visto. Il server rilegge carrello e catalogo, ricalcola tutto, rifiuta (`Conflitto`) se il totale visto è diverso o se un articolo non è più disponibile, valida l'indirizzo (obbligatorio per la consegna con paese nell'area del metodo; scartato per il ritiro), svuota il carrello e crea l'ordine. Due invii della stessa cassa creano un solo ordine (le righe del carrello si tolgono per prime e devono essere tutte quelle lette). La giacenza gestita si prenota nella stessa transazione (sezione Giacenza e prenotazioni).
- **Chi:** ordinano gli account autenticati e attivi come per il carrello; ognuno vede e annulla solo i propri ordini (un ordine altrui è `404`, come uno inesistente). Il cliente può annullare solo un ordine non ancora pagato.
- **Pagine:** `/account/checkout` (cassa), `/account/ordini`, `/account/ordini/{id}` (con annullamento); in amministrazione `/admin/negozio/ordini` e `/admin/negozio/ordini/{id}`, **sola lettura** (gli ultimi 200 ordini; le azioni di evasione arrivano con il pagamento). L'indirizzo si scrive in campi di testo con il paese come codice a due lettere (un elenco di nomi dei paesi è un miglioramento successivo).
- **Non ancora:** (vedi le sezioni successive: pagamenti, rimborsi, dati personali).

## Giacenza e prenotazioni (M7.6, F-005, ADR-0020)

- **Gestione facoltativa per variante.** Senza una riga di giacenza una variante si vende senza limite (servizi, prodotti digitali); con la riga (`GiacenzeVarianti`) `Disponibile` è la quantità fisica e `Riservata` la parte trattenuta da prenotazioni attive. Il vincolo `CK_GiacenzeVarianti_Quantita` del database impone `0 ≤ Riservata ≤ Disponibile ≤ 1.000.000`: nessun percorso di codice può vendere più di quanto c'è, nemmeno aggirando il servizio (test).
- **Prenotazione atomica (15 minuti).** Alla creazione dell'ordine, nella stessa transazione, ogni riga con giacenza gestita esegue `UPDATE … SET Riservata = Riservata + @q WHERE Disponibile − Riservata ≥ @q`: una sola istruzione condizionale, mai lettura e poi scrittura. Se anche una riga non basta, la transazione si annulla: non nasce l'ordine, il carrello resta com'è e la risposta (`Conflitto`) elenca i codici mancanti (`Giacenza:CODICE`). Le varianti si trattano in ordine fisso per non creare blocchi incrociati. Prova con 15 acquirenti contemporanei su 5 pezzi: ne passano esattamente 5.
- **Una prenotazione per riga d'ordine** (`PrenotazioniGiacenza`: stato `Attiva`/`Confermata`/`Rilasciata`, scadenza); l'ordine mostra «riservata fino alle» (`Ordine.PrenotazioneScadeIlUtc`, nulla se nessuna riga ha giacenza gestita; gli ordini senza giacenza gestita non scadono).
- **Rilascio.** Alla scadenza, su annullamento da parte del cliente o per pagamento fallito: ogni prenotazione si chiude con un'istruzione condizionale (`Attiva` → `Rilasciata`) e solo allora la quantità torna libera, quindi due rilasci insieme non la restituiscono due volte. La scadenza si applica (a) a ogni creazione d'ordine, prima di prenotare, (b) quando l'amministrazione apre la pagina delle giacenze e (c) nella manutenzione periodica (`ManutenzioneDati`): annulla gli ordini ancora non pagati (`InAttesaDiPagamento`/`PagamentoFallito` → `Annullato`, audit `ScadenzaOrdine` senza attore) e non tocca mai un ordine già pagato.
- **Conferma** (`IServizioGiacenze.ConfermaOrdineAsync`, per M7.7): all'incasso la prenotazione diventa `Confermata`, `Disponibile` e `Riservata` scendono insieme e resta un movimento `Vendita` (`MovimentiGiacenza`, storico che sopravvive alla variante). Se una prenotazione dell'ordine era già stata rilasciata (pagamento arrivato dopo la scadenza) risponde `Conflitto` e non cambia nulla: la slice del pagamento dovrà decidere (rimborso o evasione manuale) e lo riconcilia con il provider.
- **Amministrazione:** `/admin/negozio/giacenze` (404 senza negozio attivo): per ogni variante si imposta la quantità fisica (non sotto quanto è riservato) o si smette di gestirla (rifiutato finché ci sono ordini che trattengono merce); ogni modifica è un movimento `Impostazione` e va nell'audit. Una variante con merce trattenuta non si elimina dal catalogo; senza prenotazioni la sua giacenza se ne va con lei.
- **Non ancora:** mostrare la disponibilità nel carrello e nelle pagine prodotto, soglie di scorta bassa, storico dei movimenti in interfaccia, prenotazione rinnovata al nuovo tentativo di pagamento (M7.7).

## Pagamenti con PayPal (M7.7, F-004, ADR-0020)

**Credenziali e client (M7.7a).**
- **Credenziali per sito** (`ImpostazioniPagamentoSiti`): ambiente (Sandbox o Produzione), identificativo client in chiaro e, **solo cifrati** con la protezione dei segreti dei canali email (ADR-0015), il segreto del client e l'identificativo del webhook. Le gestisce il `PlatformAdmin` da `/admin/platform/tenant/{id}/pagamenti?idSito=…` (link nel dettaglio del tenant, per i siti con negozio attivo): in sola scrittura, un campo lasciato vuoto non cambia il valore salvato, la pagina mostra soltanto se sono impostati. Nell'audit finiscono ambiente e identificativo client, mai i segreti; la classe delle credenziali non li mostra nemmeno in `ToString`. Se la chiave dei segreti cambia, i segreti risultano non impostati e vanno reinseriti.
- **Client** (`IProviderPagamento`, implementazione `ClientePayPal` su Orders v2): crea l'ordine di pagamento con importo e valuta decisi dal server (solo EUR, importo positivo, due decimali), `PayPal-Request-Id` per l'idempotenza, `NO_SHIPPING` e `PAY_NOW`; cattura; legge lo stato di un ordine; verifica la firma di un webhook con il servizio `verify-webhook-signature` e l'identificativo del webhook del conto. L'URL di approvazione si accetta solo se è HTTPS su `paypal.com`; gli identificativi d'ordine si validano prima di diventare parte di un percorso. Gli errori riportano solo il codice HTTP e l'identificativo di diagnosi del provider, mai il corpo della risposta né segreti. Nessun dato di carta o di conto passa dal nostro sistema.
- **Test:** `PayPalFinto` (gestore HTTP con stato che riproduce token, creazione idempotente, approvazione, cattura, lettura e verifica webhook); nessuna chiamata di rete reale e nessuna credenziale vera.

**Pagamenti, cattura e webhook (M7.7b).**
- **Avvio** (`IServizioPagamenti.AvviaAsync`, dalla pagina dell'ordine «Paga con PayPal»): per un ordine proprio in attesa di pagamento (o con pagamento rifiutato, che torna in attesa) il server crea l'ordine di pagamento presso PayPal con **importo e valuta dell'ordine** (mai dal browser) e registra un `TentativoPagamento` il cui identificativo è anche il `PayPal-Request-Id`. Il cliente prosegue da un collegamento verso PayPal (non da un reindirizzamento dopo un modulo: la politica dei contenuti limita i moduli al sito stesso). Gli indirizzi di ritorno e di annullo usano il dominio registrato del sito.
- **Ritorno e cattura** (`/account/ordini/{id}/pagamento?token=…`): il token serve solo a ritrovare il tentativo del proprietario; il server incassa (idempotente) e legge l'esito. Un ordine già pagato con un altro tentativo, annullato o scaduto **non si incassa affatto**; un incasso in sospeso lascia l'ordine in attesa; un rifiuto lo porta in `PagamentoFallito`.
- **Pagato solo a condizioni.** L'ordine diventa `Pagato` solo se il provider ha incassato **esattamente** l'importo e la valuta dovuti; nella stessa transazione la prenotazione di giacenza si conferma. Se l'importo è diverso, o l'ordine è stato chiuso nel frattempo, o la merce non è più riservata, il tentativo diventa `DaRimborsare` (in evidenza nella vista amministrativa) e l'ordine non risulta pagato: finché non è approvata la politica A5 il rimborso è manuale dal conto PayPal del negozio.
- **Webhook** (`POST /webhooks/paypal` sul dominio del sito, pubblico): corpo limitato a 256 KB; senza le cinque intestazioni di firma → 401; la **firma si verifica con il servizio del provider** e l'identificativo del webhook del conto prima di leggere il contenuto (firma non valida → 401, errore del provider → 503 perché lo ripeta). L'evento si **deduplica** (`EventiWebhook`, indice unico sito+provider+evento; non si conserva il contenuto, che può avere dati personali del cliente): uno già elaborato non fa nulla, uno ricevuto ma mai concluso si rielabora. Il contenuto non decide nulla: per `CHECKOUT.ORDER.APPROVED` il server incassa, per `PAYMENT.CAPTURE.COMPLETED/DENIED/DECLINED` rilegge lo stato dal provider. Eventi di altri tipi o riferiti a pagamenti sconosciuti → 200 ignorati.
- **Concorrenza.** Ritorno del cliente e webhook possono arrivare insieme, ripetuti o in disordine: ogni passaggio (tentativo, ordine, giacenza) è un aggiornamento condizionale, quindi si incassa e si conferma una volta sola (test con tre elaborazioni in parallelo).
- **Checkout di produzione per installazione.** Con credenziali in ambiente di produzione il pagamento non parte finché la configurazione `Negozio:CheckoutProduzione` non è `true` (predefinito falso): è un interruttore di esercizio che si accende dopo le verifiche (A5 e A7 sono approvate). Il Sandbox funziona sempre.
- **Email di conferma:** quando l'ordine risulta pagato si accoda nell'outbox, **nella stessa transazione** del pagamento, una email al cliente (articoli, spedizione, totale, indirizzo o ritiro, collegamento all'ordine sul dominio del sito), cifrata a riposo perché contiene l'indirizzo e nella lingua in cui il cliente ha acquistato (`Ordine.CodiceLingua`, solo se abilitata nel sito, altrimenti la predefinita). Un solo messaggio per ordine (chiave di idempotenza); se il tenant non ha un canale email configurato non si accoda nulla e il pagamento non ne dipende. Il collegamento usa `https`: in sviluppo con porte o http il link va corretto a mano.
- **Evasione:** in `/admin/negozio/ordini/{id}` il tenant vede i tentativi di pagamento e, per un ordine pagato, segna «spedito» (consegna) o «ritirato» (ritiro in sede) con un passaggio condizionale e audit.
- **Non ancora:** riconciliazione periodica con il provider per gli incassi mai notificati, pagamenti di ordini a totale zero, mostrare i pagamenti nell'elenco ordini.

## Vetrina pubblica (M7.8, F-001)

- **Pagine:** `/negozio` (elenco) e `/negozio/{id}` (scheda con varianti), pubbliche e senza cache (dipendono dall'acquirente). Il percorso `negozio` (come `webhooks`) è **riservato**: una pagina del CMS non può usarlo come slug.
- **Cosa si vede:** solo prodotti **pubblicati** con almeno una variante **attiva**, solo se il sito ha il negozio attivo (altrimenti 404); mai bozze, archiviati, varianti disattivate o dati di altri tenant. Nomi, etichette e descrizione nella lingua della richiesta con ripiego sulla lingua predefinita; il testo è sempre codificato (nessun HTML).
- **Disponibilità:** per giacenza gestita la quantità richiedibile è quella libera (disponibile meno riservata, al più 99); zero = «Esaurito». La giacenza esatta non è esposta. Resta un controllo di cortesia: la garanzia vera è la prenotazione alla creazione dell'ordine.
- **Aggiungi al carrello:** l'acquirente autenticato vede un modulo che invia a `/account/carrello?handler=Aggiungi` **solo identificativo della variante e quantità** (prezzi e totali restano calcolati dal server); la pagina assume l'identità dell'acquirente prima di emettere il token antiforgery, che è legato all'utente. L'anonimo vede il collegamento «Accedi per acquistare» (con ritorno alla scheda).
- **Immagini dei prodotti (M7.15).** Fino a 8 risorse della libreria media per prodotto, in ordine (la prima è la principale), scelte nel modulo del prodotto con un numero accanto a ogni immagine pubblica (senza JavaScript). Il server accetta solo immagini pubbliche, non in quarantena, del sito dell'host e senza ripetizioni; `Immagini` nullo non tocca quelle esistenti. Il collegamento (`ImmaginiProdotto`) non ha chiave esterna verso i media, come le immagini del builder: eliminare una risorsa dalla libreria non blocca né rompe il prodotto, e la vetrina mostra solo le risorse ancora pubbliche. Elenco: l'immagine principale; scheda: tutte, con il testo alternativo della risorsa (ripiego sul nome del prodotto); la griglia e la scheda del builder usano la principale.
- **Non ancora:** categorie e collezioni, ricerca, voce di menu verso `/negozio` nei temi, indicizzazione/sitemap dei prodotti. La griglia di prodotti per il page builder esiste (`negozio.griglia-prodotti`, vedi `page-builder.md`); esiste anche la scheda di un prodotto scelto (`negozio.scheda-prodotto`); l'acquisto resta in `/negozio/{id}`.

## Rimborsi (A5, approvati il 2026-10-11)

- **Dal pannello del tenant** (`/admin/negozio/ordini/{id}`): rimborso totale o parziale di un ordine pagato, spedito o ritirato; importo facoltativo (vuoto = tutto ciò che resta), al più due decimali e mai oltre quanto resta; motivo facoltativo (≤200 caratteri) che si comunica anche al cliente come nota di PayPal. Solo `TenantAdmin` del tenant o `PlatformAdmin`, ruolo riverificato a ogni operazione, audit di richiesta ed esito.
- **Importo impegnato prima del provider.** `Ordini.TotaleRimborsato` si incrementa con un aggiornamento condizionale (il database impone `0 ≤ rimborsato ≤ totale`) **prima** di chiamare PayPal: richieste concorrenti non possono rimborsare più del totale (test con quattro richieste in parallelo). Un rifiuto del provider libera l'importo.
- **Idempotenza ed esito incerto.** Il rimborso (`Rimborsi`) ha come identificativo il `PayPal-Request-Id`. Se la risposta si perde o PayPal lo sta ancora elaborando, il rimborso resta «in corso» con l'importo impegnato e il pulsante «Verifica l'esito» (o il webhook `PAYMENT.CAPTURE.REFUNDED` / `PAYMENT.REFUND.*`, che rilegge lo stato dal provider) lo conclude senza rimborsare due volte. Un rimborso fatto direttamente dal conto PayPal, non richiesto da qui, non si conosce e il webhook lo ignora.
- **Stato dell'ordine.** Diventa `Rimborsato` (finale) quando i rimborsi completati coprono il totale; i parziali lo lasciano com'è e il cliente vede «Rimborsato: X». A rimborso completato parte una email al cliente (stessa cifratura e lingua dell'ordine).
- **Incasso da rimborsare.** Un incasso che non ha potuto diventare un ordine pagato (importo diverso, ordine chiuso, merce non più riservata) si restituisce dal pulsante «Rimborsa l'incasso» (importo realmente incassato, `TentativiPagamento.ImportoIncassato`); non tocca l'ordine.
- **Non ancora:** reinserimento automatico in giacenza (lo fa il tenant), rimborsi per articolo, riconciliazione dei rimborsi fatti fuori dal sistema.

## Dati personali degli acquirenti (A7, approvati il 2026-10-11)

- **Pagina «I miei dati»** (`/account/dati`, area cliente): scarica in JSON i dati dell'account e dei propri ordini (portabilità); elimina l'account con riconferma della password e casella di conferma. La riconferma conta i tentativi falliti come un accesso (stesso blocco temporaneo). Un amministratore che compra con la sessione cliente (ADR-0011) non si elimina da qui.
- **Anonimizzazione dell'account** (`IServizioDatiPersonali`): email e nome sono sostituiti da valori non riconducibili (`anonimo-<id>@anonimizzato.invalid`, «Account eliminato»), la password diventa inutilizzabile, il secondo fattore e i token pendenti (conferme, recuperi) si cancellano, lo stato diventa `Anonimizzata` (irreversibile: neppure il `PlatformAdmin` la riattiva) e il carrello si svuota. La riga dell'account resta perché gli ordini la referenziano. Gli **ordini non pagati si annullano** nella stessa transazione e liberano la giacenza; gli ordini **pagati restano** con la copia dei dati, per gli obblighi fiscali. L'indirizzo email torna libero per una nuova registrazione.
- **A richiesta dell'interessato a un amministratore:** dal dettaglio di un ordine (`/admin/negozio/ordini/{id}`, sezione «Dati personali dell'acquirente») il `TenantAdmin` del tenant o il `PlatformAdmin` anonimizza l'account di chi ha fatto l'ordine, senza password e con audit.
- **Conservazione degli ordini:** `Negozio:ConservazioneOrdiniAnni` (predefinito **10**). La manutenzione periodica (`ManutenzioneDati`) anonimizza, in tutti i tenant, gli ordini non modificati da più di quel numero di anni: email, nome e indirizzo si cancellano (`Ordini.AnonimizzatoIlUtc`), restano numero, date, stato, importi, righe e paese, che servono alla contabilità. Operazione idempotente, con audit per tenant.
- **Informative:** privacy e condizioni di reso sono **pagine normali del sito, scritte dal tenant**, con gli slug convenzionali `privacy` e `resi`: se la pagina è pubblicata nella lingua della richiesta, il piè di pagina della vetrina e dell'area cliente la collega (altrimenti nessun collegamento). Il sistema non fornisce testi legali.
- **Non ancora:** esportazione in PDF, richiesta di cancellazione per email dal modulo di contatto, registro dei consensi per le comunicazioni commerciali (il negozio invia solo email transazionali).

## Integrazione con il builder

I componenti `negozio.*` referenziano prodotto/collezione/query tipizzata, non copiano il catalogo nel documento. Il renderer chiama il modulo Store attraverso servizi applicativi. Un'anteprima senza dati reali deve essere chiaramente distinta dal checkout reale.

## Decisioni

Decise con [ADR-0020](../decisions/ADR-0020-commerce-policies-and-paypal.md): provider **PayPal** (Orders v2, webhook con firma verificata e deduplicati, credenziali per tenant cifrate), solo **EUR** con prezzi **IVA inclusa** e aliquota per prodotto, **tariffa fissa per zona e ritiro in sede**, **prenotazione della giacenza con scadenza di 15 minuti**.

Tutte le decisioni commerciali sono prese (ADR-0020, voci 1–10): provider, mercati, IVA, spedizioni, inventario, rimborsi, dati personali, macchina a stati, amministratori come acquirenti. Restano scelte di esercizio (credenziali del conto reale, registrazione del webhook, accensione di `Negozio:CheckoutProduzione`) e le rifiniture elencate sotto «Non ancora» in ogni sezione.

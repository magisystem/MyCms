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
- **Ancora da fare:** pagine di recupero password per i clienti (oggi il flusso è `/admin/recupero`, valido per `Utente` ma con l'aspetto dell'admin) e accesso degli amministratori come acquirenti (ADR-0011).

## Carrello e totali (M7.3, F-002)

- **Il cliente invia solo identificativi e quantità.** `IServizioCarrello` non ha alcun parametro per prezzi, aliquote o totali: li legge sempre dal catalogo attuale. Un prezzo, un'aliquota o un totale aggiunti al modulo si ignorano (test). Il carrello (`Carrelli`, `RigheCarrello`) non conserva importi.
- **Chi:** carrello personale, uno per account e per sito; ordina chi è autenticato e attivo (`Utente` o `TenantAdmin` del tenant dell'host, `PlatformAdmin` ovunque, ADR-0011). Il `Guest` non ha carrello (nessun guest checkout). Per ora il sito pubblico apre l'area cliente solo agli `Utente`.
- **Limiti:** 1–99 unità per riga, 50 righe diverse; oltre, `DatiNonValidi`. Si acquista solo una variante attiva di un prodotto pubblicato del sito dell'host: bozza, archiviata, disattivata o di un altro tenant danno `NonTrovato`.
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

## Eventi e stati (proposta, M7.5)

Gli stati sono un'enumerazione chiusa (`StatoOrdine`) e **ogni passaggio** passa da `TransizioniOrdine` (dominio): un passaggio non elencato non esiste. Lo stato non si modifica con un'assegnazione libera.

| Da | A | Chi / quando |
|---|---|---|
| `InAttesaDiPagamento` | `Pagato` | cattura PayPal riuscita (M7.7) |
| `InAttesaDiPagamento` | `PagamentoFallito` | pagamento rifiutato o scaduto (M7.7) |
| `InAttesaDiPagamento` | `Annullato` | il cliente annulla, o scade la prenotazione (M7.6) |
| `PagamentoFallito` | `InAttesaDiPagamento` | nuovo tentativo di pagamento (M7.7) |
| `PagamentoFallito` | `Annullato` | il cliente annulla, o scade la prenotazione |
| `Pagato` | `Spedito` | il tenant segna la spedizione (solo metodo a zona) |
| `Pagato` | `Ritirato` | il tenant segna il ritiro (solo metodo ritiro) |

`Spedito`, `Ritirato` e `Annullato` sono **finali**. Un ordine pagato non si annulla: si rimborsa, e gli stati di rimborso (`RimborsoInCorso`, `Rimborsato`, con i passaggi `Pagato` → `RimborsoInCorso` → `Rimborsato`) si aggiungono solo quando sarà approvata la politica A5. Un tentativo di pagamento è un'entità a parte (`TentativoPagamento`, M7.7); l'ordine cambia stato solo in risposta a esiti verificati (cattura lato server o webhook firmato e deduplicato), mai su dichiarazione del browser. Ogni transizione è una scrittura con controllo di concorrenza (`TokenConcorrenza`) e finisce nell'audit.

**Da approvare** dal committente (vedi `docs/decisioni-da-confermare.md`, C): questa tabella, in particolare l'assenza di annullamento di un ordine pagato e di uno stato "in preparazione".

## Ordini (M7.5, F-003)

- **Snapshot:** `Ordini` e `RigheOrdine` conservano una copia di tutto ciò che conta al momento dell'acquisto — per riga codice, nome ed etichetta nella lingua del cliente, prezzo unitario lordo, aliquota, lordo, imponibile, IVA; per l'ordine metodo di spedizione (tipo, area, nome, importo scorporato), nome ed email dell'acquirente, indirizzo — e **non hanno chiavi verso varianti o metodi di spedizione**: catalogo e tariffe si possono modificare o eliminare senza toccare lo storico (test). La variante resta come semplice riferimento (`IdVariante`, senza vincolo) per la giacenza.
- **Totali:** comprendono la spedizione; il database impone `lordo = imponibile + IVA` per riga, per spedizione e per ordine, e la coerenza fra tipo di consegna, area e indirizzo (`CK_Ordini_*`, `CK_RigheOrdine_*`).
- **Numero d'ordine:** progressivo per sito, senza buchi né duplicati: si prende incrementando `Siti.UltimoNumeroOrdine` nella stessa transazione che crea l'ordine (indice unico `(IdSito, Numero)`).
- **Creazione** (`IServizioOrdini.CreaDaCarrelloAsync`, un'unica transazione): il cliente invia solo metodo di spedizione, indirizzo e il totale che ha visto. Il server rilegge carrello e catalogo, ricalcola tutto, rifiuta (`Conflitto`) se il totale visto è diverso o se un articolo non è più disponibile, valida l'indirizzo (obbligatorio per la consegna con paese nell'area del metodo; scartato per il ritiro), svuota il carrello e crea l'ordine. Due invii della stessa cassa creano un solo ordine (le righe del carrello si tolgono per prime e devono essere tutte quelle lette). Senza giacenza: la prenotazione arriva con M7.6.
- **Chi:** ordinano gli account autenticati e attivi come per il carrello; ognuno vede e annulla solo i propri ordini (un ordine altrui è `404`, come uno inesistente). Il cliente può annullare solo un ordine non ancora pagato.
- **Pagine:** `/account/checkout` (cassa), `/account/ordini`, `/account/ordini/{id}` (con annullamento); in amministrazione `/admin/negozio/ordini` e `/admin/negozio/ordini/{id}`, **sola lettura** (gli ultimi 200 ordini; le azioni di evasione arrivano con il pagamento). L'indirizzo si scrive in campi di testo con il paese come codice a due lettere (un elenco di nomi dei paesi è un miglioramento successivo).
- **Non ancora:** pagamento, prenotazione della giacenza, email di conferma ordine, evasione (`Spedito`/`Ritirato`) dall'amministrazione, accesso degli amministratori come acquirenti dall'area cliente (ADR-0011: il servizio li ammette già, l'area cliente oggi apre solo agli `Utente`).

## Integrazione con il builder

I componenti `negozio.*` referenziano prodotto/collezione/query tipizzata, non copiano il catalogo nel documento. Il renderer chiama il modulo Store attraverso servizi applicativi. Un'anteprima senza dati reali deve essere chiaramente distinta dal checkout reale.

## Decisioni

Decise con [ADR-0020](../decisions/ADR-0020-commerce-policies-and-paypal.md): provider **PayPal** (Orders v2, webhook con firma verificata e deduplicati, credenziali per tenant cifrate), solo **EUR** con prezzi **IVA inclusa** e aliquota per prodotto, **tariffa fissa per zona e ritiro in sede**, **prenotazione della giacenza con scadenza di 15 minuti**.

## Decisioni ancora aperte

- Politica resi e rimborsi (proposta: rimborso totale o parziale dal pannello, tramite il provider).
- Gestione dei dati personali degli acquirenti, conservazione e obblighi legali (proposta: ordini 10 anni, account anonimizzabile su richiesta).
- Approvazione della macchina a stati dell'ordine proposta sopra.

Non implementare checkout di produzione finché le decisioni pertinenti non sono approvate e testate.

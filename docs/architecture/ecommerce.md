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
- **Ancora da fare:** pagine di recupero password per i clienti (oggi il flusso è `/admin/recupero`, valido per `Utente` ma con l'aspetto dell'admin), accesso degli amministratori come acquirenti (ADR-0011, con la slice dell'ordine), ordini nell'area cliente.

## Carrello e totali (M7.3, F-002)

- **Il cliente invia solo identificativi e quantità.** `IServizioCarrello` non ha alcun parametro per prezzi, aliquote o totali: li legge sempre dal catalogo attuale. Un prezzo, un'aliquota o un totale aggiunti al modulo si ignorano (test). Il carrello (`Carrelli`, `RigheCarrello`) non conserva importi.
- **Chi:** carrello personale, uno per account e per sito; ordina chi è autenticato e attivo (`Utente` o `TenantAdmin` del tenant dell'host, `PlatformAdmin` ovunque, ADR-0011). Il `Guest` non ha carrello (nessun guest checkout). Per ora il sito pubblico apre l'area cliente solo agli `Utente`.
- **Limiti:** 1–99 unità per riga, 50 righe diverse; oltre, `DatiNonValidi`. Si acquista solo una variante attiva di un prodotto pubblicato del sito dell'host: bozza, archiviata, disattivata o di un altro tenant danno `NonTrovato`.
- **Disponibilità:** un articolo che perde disponibilità (prodotto non più pubblicato, variante disattivata) resta visibile come "non più disponibile", fuori dai totali, non si può aumentare e si può togliere. Una variante eliminata dal catalogo scompare anche dai carrelli (gli ordini avranno snapshot e una variante già ordinata si disattiva soltanto).
- **Totali** (`CalcolatoreTotali`, puro e con test sistematici): il lordo di riga è prezzo × quantità; l'imponibile di riga è il lordo diviso (1 + aliquota/100), arrotondato al centesimo (lontano dallo zero); l'IVA di riga è la differenza (lordo = imponibile + IVA per riga e nei totali); i totali sono somme delle righe, non un nuovo scorporo. Importi `decimal`, valuta EUR.
- **Pagina:** `/account/carrello` (mai in cache, azioni con antiforgery). Il pulsante "aggiungi" delle pagine prodotto arriverà con i componenti `negozio.*` del builder: userà `POST /account/carrello?handler=Aggiungi` con `idVariante` e `quantita`.
- **Non ancora:** scelta del metodo di consegna nel checkout (slice dell'ordine); i metodi esistono già (sezione seguente).

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

## Eventi e stati

Definire gli stati consentiti come transizioni esplicite (es. `InAttesaDiPagamento`, `Pagato`, `PagamentoFallito`, `Annullato`, `RimborsoInCorso`, `Rimborsato`, `InAttesaDiEvasione`, `Spedito`). La macchina a stati definitiva è da approvare con i requisiti commerciali; non consentire mutazioni arbitrarie dello stato.

## Integrazione con il builder

I componenti `negozio.*` referenziano prodotto/collezione/query tipizzata, non copiano il catalogo nel documento. Il renderer chiama il modulo Store attraverso servizi applicativi. Un'anteprima senza dati reali deve essere chiaramente distinta dal checkout reale.

## Decisioni

Decise con [ADR-0020](../decisions/ADR-0020-commerce-policies-and-paypal.md): provider **PayPal** (Orders v2, webhook con firma verificata e deduplicati, credenziali per tenant cifrate), solo **EUR** con prezzi **IVA inclusa** e aliquota per prodotto, **tariffa fissa per zona e ritiro in sede**, **prenotazione della giacenza con scadenza di 15 minuti**.

## Decisioni ancora aperte

- Politica resi e rimborsi (proposta: rimborso totale o parziale dal pannello, tramite il provider).
- Gestione dei dati personali degli acquirenti, conservazione e obblighi legali (proposta: ordini 10 anni, account anonimizzabile su richiesta).
- Macchina a stati definitiva dell'ordine (si approva con la slice dell'ordine).

Non implementare checkout di produzione finché le decisioni pertinenti non sono approvate e testate.

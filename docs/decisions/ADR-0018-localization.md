# ADR-0018 — Localizzazione dei testi (italiano e inglese)

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Il prodotto deve essere disponibile in più lingue, inizialmente **italiano** (lingua di base) e **inglese**. Servono due tipi di testo: i testi statici scritti nelle viste e nel codice, e i testi dinamici creati dagli utenti dal backend (pagine, prodotti, ecc.).

## Decisione

### Lingue

Tabella `Lingue` (`Id`, `Codice` `it`/`en`, `Nome`, `Cultura`, `Predefinita`, `Attiva`), popolata dal seeding; l'italiano è la lingua di base e predefinita. Altre lingue si aggiungono come righe, senza modificare il codice. Ogni sito abilita le proprie lingue in `LingueSito`. La cultura (`it-IT`, `en-GB`) governa formattazione di date e numeri; gli importi usano sempre la valuta esplicita (`database.md`).

### Testi statici

- **Nelle viste Razor:** `@_L["TESTO"]`, dove `_L` è il localizzatore (`ILocalizzatore`, iniettato in `_ViewImports.cshtml`) e `TESTO` è il testo statico in italiano, che è anche la chiave. Con parametri: `@_L["Benvenuto {0}", nome]` (formato composito; la chiave è il modello con i segnaposto).
- **Nel codice** (messaggi, validazioni, email): lo stesso localizzatore, con la lingua corrente o una lingua esplicita (per l'invio email in background, la lingua del destinatario).
- **Archiviazione:** tabella `TestiStatici` nel database (non file di risorse). Una riga per chiave e lingua.
- **Algoritmo:**
  1. se esiste la riga per la lingua corrente e la chiave, si restituisce il suo `Testo`;
  2. se manca, la riga viene creata nel database con `Testo` uguale alla chiave (testo italiano di base) e `DaTradurre` vero, e si restituisce il testo italiano;
  3. se la chiave non esiste affatto, si creano le righe di tutte le lingue attive: italiano (`DaTradurre` falso) e le altre con il testo italiano (`DaTradurre` vero).
  La creazione è sicura rispetto alla concorrenza (indice univoco `(IdLingua, HashChiave)`, un duplicato è ignorato).
- **Solo testi letterali.** L'argomento di `_L[...]` deve essere una costante nel codice sorgente. I dati degli utenti o dei visitatori non passano mai dal localizzatore: eviterebbero la creazione illimitata di righe e l'inserimento di contenuto non attendibile. Un test automatico (o un analyzer) lo verifica.
- **Prestazioni:** i testi sono tenuti in una cache in memoria per lingua; la lettura non interroga il database a ogni richiesta. La cache si invalida quando un testo viene modificato (con più istanze serve un meccanismo condiviso, vedi `operations.md`).
- **Sicurezza:** il risultato è codificato in HTML dal motore Razor; i testi sono testo semplice. HTML nei testi tradotti non è ammesso (un'eventuale variante con HTML sanificato richiederà un nuovo ADR).
- **Ambito globale:** `TestiStatici` è comune a tutti i tenant, senza `IdTenant`; non esistono sostituzioni per tenant. Nelle chiavi e nei testi non vanno dati di tenant o personali.

### Modifica dei testi statici

Il menu **Platform** ha una schermata "Testi" riservata al `PlatformAdmin`: elenco con ricerca e filtro per lingua e per "da tradurre", modifica del testo, salvataggio che azzera `DaTradurre` e invalida la cache, voce in `RegistroAudit`. Il `TenantAdmin` non può modificare i testi statici.

### Testi dinamici

- **Pagine:** una riga `Pagina` per lingua, collegate da `IdGruppoTraduzione`; ogni lingua ha revisioni, SEO (titolo, descrizione, canonical) e slug propri. Se una pagina non esiste nella lingua richiesta, la risposta è 404: niente copie automatiche o contenuto duplicato; il selettore di lingua mostra solo le traduzioni esistenti. Le pagine collegate si segnalano con `hreflang`.
- **Altre entità** (prodotti, categorie, collezioni, testo alternativo dei media, ecc.): tabelle `Traduzioni<Entità>` con chiave `(IdEntità, IdLingua)`; se manca la traduzione si usa la lingua predefinita del sito.
- Nell'admin la modifica avviene per lingua (schede o selettore).

### Scelta della lingua (assunzione da confermare)

- **Admin:** preferenza dell'account, predefinita italiano.
- **Sito pubblico:** lingua dedotta dal prefisso del percorso (`/en/...`); senza prefisso, la lingua predefinita del sito. Il selettore di lingua è esplicito.

## Alternative considerate

- File di risorse (`.resx`): richiedono una ricompilazione per cambiare un testo e non sono modificabili dal `PlatformAdmin`.
- Chiavi simboliche (`Menu.Home`): più stabili ai cambi di testo ma meno immediate; si è scelto il testo italiano come chiave.
- Testi per tenant: rimandati, non richiesti.

## Conseguenze

- Il localizzatore deve esistere dal primo layout dell'admin (M0): ogni testo visibile passa da `_L`.
- Se si modifica un testo letterale nella vista, nasce una nuova chiave e la vecchia resta nel database; servirà uno strumento di pulizia delle chiavi non usate (non prioritario).
- Più righe e scritture iniziali sul database; con la cache il costo è limitato.
- I testi del builder in JavaScript e le email richiedono il localizzatore anche fuori dalle viste; il builder riceverà un dizionario dal server (M4).

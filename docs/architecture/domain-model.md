# Modello di dominio e dati

Questo è il modello logico iniziale; i nomi e i tipi definitivi vanno tradotti in entity/configuration EF Core durante il bootstrap. Non è necessario che ogni riga diventi subito una tabella autonoma.

## Tenant, sito e dominio

- `Tenant`: cliente/organizzazione contrattuale della piattaforma. Il tenant primario (di gestione) ha `Primario` vero: ne esiste uno solo ed è creato dal seeding (ADR-0014).
- `Sito`: sito appartenente a un tenant. Un tenant può avere più siti.
- `DominioSito`: host registrato, normalizzato (minuscolo, senza porta, punycode; vincolo `CK_DominiSito_HostMinuscolo`), unico in tutta la piattaforma e associato a un sito tramite chiave esterna composta `(IdTenant, IdSito)` (un dominio non può puntare al sito di un altro tenant), con `VerificatoIlUtc`, `VerificatoDaIdUtenza` e stato attivo/disattivo. La verifica è manuale (ADR-0014).
- `Utenza`: account locale (tabella `Utenze`, ADR-0016). Colonna `Ruolo` (`PlatformAdmin`, `TenantAdmin`, `Utente`). Il `PlatformAdmin` è globale (`IdTenant` nullo); `TenantAdmin` e `Utente` hanno `IdTenant` e appartengono a un solo tenant. Un tenant può avere più `TenantAdmin`. Vincolo: `PlatformAdmin` ⇔ `IdTenant` nullo. Indice univoco su `(IdTenant, EmailNormalizzata)`: la stessa email può esistere in tenant diversi; fra i `PlatformAdmin` l'email è unica perché in SQL Server i NULL sono considerati uguali. Contiene hash della password, `TimbroSicurezza`, stato, contatore dei tentativi falliti e blocco temporaneo.
- `ImpostazioniEmailTenant`: `ProviderEmail` del tenant (`Smtp`, `Graph`, `Gmail`, `SendGrid`) e impostazioni del canale scelto; segreti cifrati (ADR-0015).
- `ProfiloGuest`: profilo del `Guest` del tenant, creato insieme al tenant, senza impostazioni: il Guest vede tutto il contenuto pubblicato.
- `Sito.RegistrazioneAcquirentiAbilitata`: abilita la registrazione degli acquirenti; vero solo se l'e-commerce è attivo (default falso).
- `RichiestaRecuperoPassword` (con `IdTenant`): richiesta di recupero con hash dell'OTP, tentativi, scadenza, hash del token del link monouso (emesso solo dopo l'OTP corretto) e stato.

Creare o elevare un `TenantAdmin` è riservato al `PlatformAdmin` (ADR-0011).

## CMS

### `Pagina`

- `Id`, `IdTenant`, `IdSito`
- `Slug`, `TipoPagina`, `Stato`
- `PaginaIniziale`, `IdLingua` (obbligatoria, FK a `Lingue`), `IdGruppoTraduzione` (le versioni nelle varie lingue della stessa pagina condividono il gruppo)
- `IdUltimaRevisioneBozza`, `IdRevisionePubblicata` (chiavi esterne composite `(Id, IdRevisione...)` verso `RevisioniPagina(IdPagina, Id)`: una pagina non può puntare alla revisione di un'altra pagina), `UltimoNumeroRevisione`
- `CreatoIlUtc`, `AggiornatoIlUtc`, `PubblicatoIlUtc`, `TokenConcorrenza` (`rowversion`)

`LingueSito` (C-008) elenca le lingue abilitate per ogni sito, con la predefinita (vedi `publishing-and-seo.md`); le pagine possono esistere solo nelle lingue abilitate.

Pagina e revisione si puntano a vicenda: EF Core non ordina inserimenti circolari, quindi una pagina nuova si salva in due passi dentro una transazione (`SalvaNuovaPaginaAsync`: prima pagina e revisione, poi i puntatori). Una pagina ha al più una `PaginaIniziale` per sito e lingua (indice univoco filtrato). La chiave esterna composta `(IdTenant, IdSito)` impedisce a una pagina di puntare al sito di un altro tenant.

Slug riservati: `admin` (area di amministrazione, `/admin`) e i percorsi degli asset statici (es. `lib`) non sono utilizzabili come slug di pagina e vanno rifiutati alla creazione. Ogni lingua di una pagina è una riga `Pagina` distinta con le proprie revisioni e il proprio slug; lo slug è univoco all'interno di sito e lingua (indice univoco su `(IdSito, IdLingua, Slug)`; `IdLingua` non è nullable, quindi non c'è ambiguità sui NULL). Il cambio di slug di una pagina già pubblicata (`PubblicataInPassato`) lascia un `RedirectPagina` dal vecchio slug (C-005): il reindirizzamento punta alla **pagina**, non a un indirizzo, quindi la destinazione è sempre lo slug attuale (un solo salto, nessuna catena); è unico per `(IdSito, IdLingua, SlugOrigine)`, ha chiave esterna composta verso la pagina, e viene tolto quando quello slug torna in uso (anche dalla stessa pagina o da una pagina nuova). I campi SEO sono per revisione: `TitoloSeo` (≤200), `DescrizioneSeo` (≤500), `NonIndicizzare`.

### `RevisionePagina`

- `Id`, `IdTenant`, `IdSito`, `IdPagina`
- `NumeroRevisione`, `VersioneSchema`
- `ProgettoEditorJson` (progetto per riaprire il builder; nullo se la revisione non usa il builder; **rigenerato dal server a partire dal documento validato**, ADR-0005)
- `DocumentoRenderJson` (documento canonico che il renderer supporta; nullo se la revisione non usa il builder)
- `SeoJson` o campi SEO strutturati
- `SalvataggioAutomatico` (bit): salvataggio automatico del builder; sostituito dal successivo se in cima, non pubblicato e dello stesso autore (ADR-0005)
- `CreatoDaIdUtenza`, `CreatoIlUtc`, `NotaRevisione`

In M1 una revisione contiene `Titolo` e `Contenuto` in testo semplice (paragrafi separati da una riga vuota, codificato in uscita, `VersioneSchema` 1); le colonne `ProgettoEditorJson`/`DocumentoRenderJson` e l'HTML ripulito arrivano con il builder e il rich text. Lo slug M1 è un solo segmento (lettere ASCII minuscole, cifre, trattini; massimo 100) e gli slug riservati sono `admin`, `lib`, `errore` e `media`. **Modelli di pagina (C-006).** Ogni revisione registra il modello e la versione con cui è stata scritta (`IdModello`, `VersioneModello`) e i valori dei campi del modello (`DatiModelloJson`, oggetto nome-valore); `Titolo` e `Contenuto` restano campi di base. I modelli sono definizioni nel codice (`RegistroModelliPagina`), non dati dei tenant: ogni **versione è immutabile** (un test fissa l'impronta SHA-256 di quelle rilasciate, e il registro rifiuta due definizioni con stesso id e versione); per cambiare un modello si aggiunge una nuova versione. Le nuove pagine usano l'ultima versione, le revisioni esistenti restano alla loro e si disegnano con la vista di quella versione; una bozza passa alla versione nuova solo su richiesta ("Passa all'ultima versione del modello", nuova revisione, campi scartati se non esistono più). Una *policy di editing* per versione (contenuto libero ammesso, lunghezza massima) e le regole dei campi (obbligatorio, lunghezza, tipo, gruppi "tutti o nessuno") sono applicate dal server a ogni salvataggio, qualunque cosa invii il client; i campi non previsti sono rifiutati. I campi di tipo indirizzo accettano solo percorsi del sito o `https://` e sono ricontrollati anche in uscita. Modelli rilasciati: `testo-semplice` v1 (nessun campo) e `presentazione` v1 (sottotitolo, testo e indirizzo del pulsante). Le viste Razor dei modelli sono in `Pages/Shared/Modelli` (`ModelliVista`).

**Modelli del builder (M4).** `pagina-libera` v1 e `pagina-con-intestazione-fissa` v1 e v2 (v2: il corpo ammette anche hero, invito all'azione e fisarmonica) sono modelli senza campi e senza testo libero (`Builder = true`, impronta propria): il contenuto è il documento del builder, vincolato dal template omonimo (`RegistroTemplateBuilder`). Una revisione del builder porta `IdModello`/`VersioneModello` del template e le due colonne JSON; modifica dei metadati (titolo, SEO), ripristino e passaggi di stato copiano il documento della revisione di partenza.

Le revisioni sono immutabili dopo la creazione. Il puntatore `IdRevisionePubblicata` della pagina cambia in una transazione.

### Template

- `Modello`: identità stabile del template, scope globale o specifico tema.
- `VersioneModello`: versione immutabile di struttura iniziale, schema e policy di editing.
- `VersioneTema`/registro tema: la versione del tema che il documento richiede o con cui è stato verificato.

I template non devono sovrascrivere contenuti già creati quando viene pubblicata una nuova versione.

## Temi

`ImpostazioniTemaSito` (o configurazione equivalente): `IdTenant`, `IdSito`, `ChiaveTema`, `VersioneTema`, `ImpostazioniJson`, `AggiornatoIlUtc`. Settings JSON deve essere validato contro lo schema dichiarato dal tema. Nessun percorso di file/assembly arbitrario proveniente dal database viene caricato come codice.

## Media

`RisorsaMedia`: ID, tenant, sito o scope, object key, nome originario, content type rilevato, dimensione, hash, dimensioni immagine, alt text, stato di scansione e timestamp. Il file binario sta nello storage, non in una colonna DB per default.

**Attuazione (D-003, D-004).** Tabella `RisorseMedia`: `IdTenant`, `IdSito` (chiave esterna composta verso `Siti`), `ChiaveOggetto` (unica, `{tenant}/{sito}/{risorsa}` in esadecimale, generata dal server), `NomeOriginale` (ripulito, solo etichetta), `TipoContenuto` (rilevato dalla firma), `Dimensione`, `HashSha256` (`binary(32)`), `Larghezza` e `Altezza`, `Visibilita` (`Pubblica`/`Privata`), `StatoScansione` (`NonEseguita`/`Pulito`/`InQuarantena`; la scansione antimalware non è ancora prevista, vedi G-005), `CreatoIlUtc`, `CreatoDaIdUtenza`, token di concorrenza. Il testo alternativo sta in `TraduzioniRisorseMedia` (chiave `(IdRisorsa, IdLingua)`, chiave esterna composta verso la risorsa, `TestoAlternativo` ≤ 500): nell'elenco si mostra quello della lingua predefinita del sito; se manca per una lingua la pagina che userà l'immagine ripiegherà su quello della lingua predefinita (ADR-0018). Filtri globali di tenant su entrambe le tabelle.

## Commerce

- `Prodotto`, `VarianteProdotto`, `CategoriaProdotto`, `CollezioneProdotto`
- `Prezzo` o prezzo per variante con valuta esplicita
- `ArticoloMagazzino` e `MovimentoMagazzino` (non trattare solo una quantità modificabile senza storico se serve audit)
- `Carrello`, `RigaCarrello`
- `Ordine`, `RigaOrdine` con snapshot di nome, SKU, prezzo, imposte/valori applicati e valuta
- `TentativoPagamento`/`Pagamento`, `Rimborso`, `Spedizione`
- `EventoWebhook` deduplicato e tracciabile

I confini di imposte, spedizioni, prenotazione della giacenza e valuta sono stabiliti da [ADR-0020](../decisions/ADR-0020-commerce-policies-and-paypal.md) (EUR, IVA inclusa, tariffa fissa per zona e ritiro, prenotazione di 15 minuti); resi, rimborsi e dati personali degli acquirenti restano da decidere prima del checkout di produzione.

**Attuazione del catalogo (M7.1, F-001, F-006).** `Sito.CommercioAttivo` (impostato dal `PlatformAdmin`; la registrazione degli acquirenti richiede il negozio attivo, vincolo `CK_Siti_RegistrazioneRichiedeCommercio`). Tabelle `Prodotti` (`Stato` bozza/pubblicato/archiviato, `AliquotaIva` fra 0, 4, 5, 10, 22 con vincolo di controllo, token di concorrenza), `VariantiProdotto` (`Sku` unico nel sito, maiuscolo; `PrezzoLordo` `decimal(18,2)` positivo IVA inclusa; `Valuta` solo `EUR`, anche con vincolo di controllo; `Attiva`, `Posizione`), `TraduzioniProdotto` (nome e descrizione per lingua) e `TraduzioniVarianteProdotto` (etichetta per lingua). Tutte con `IdTenant` e `IdSito`, chiavi esterne composte, filtro globale di tenant e nessuna cancellazione a cascata. Il servizio `IServizioCatalogo` salva prodotto, traduzioni e varianti in una sola transazione, valida tutto lato server (prezzo con al più due decimali, codici validi e non ripetuti, lingue del sito, nome nella lingua predefinita, almeno una variante attiva per pubblicare), controlla la concorrenza e riverifica l'attore a ogni operazione; l'amministrazione è in `/admin/negozio/prodotti`, visibile solo se il negozio è attivo. **Carrello (M7.3, F-002).** `Carrelli` (uno per account e sito, indice unico `(IdSito, IdUtenza)`, token di concorrenza) e `RigheCarrello` (chiave `(IdCarrello, IdVariante)`, `Quantita` fra 1 e 99 con vincolo di controllo); nessun importo salvato. Categorie e collezioni, giacenza e ordini arrivano con le slice successive.

## Tabelle operative

- `MessaggioOutbox`: evento registrato atomicamente con la transazione di dominio.
- `RegistroAudit`: attore, azione, risorsa, tenant, esito e timestamp; evitare segreti e payload sensibili.
- `Reindirizzamento`: sito, source path, destination, status code, data di creazione.

## Denominazione in italiano

Tutto ciò che è nostro è in italiano (ADR-0017): tabelle, colonne, classi, proprietà, metodi, namespace interni, chiavi di configurazione e valori di stato. I nomi dei progetti (`DvCms.Domain`, `DvCms.Application`, ...) restano in inglese. Restano in inglese anche le API e i nomi del framework e delle librerie (`DbContext`, `ILogger`, `CancellationToken`, `ToTable`, ecc.), i nomi dei prodotti (`Smtp`, `Graph`, `Gmail`, `SendGrid`), i nomi dei ruoli stabiliti dal committente (`PlatformAdmin`, `TenantAdmin`, `Utente`, `Guest`), il termine `Tenant`, lo `Slug`, i nomi degli ambienti ASP.NET (`Development`, `Production`) e il comando `seed`.

Convenzioni per il database:

- Tabelle: PascalCase, plurale, senza accenti, spazi, apostrofi o trattini bassi; mappatura esplicita nella configurazione EF Core (`ToTable("...")`).
- Colonne e proprietà: PascalCase italiano, singolare. Le chiavi esterne iniziano con `Id` (`IdTenant`, `IdSito`, `IdPagina`, `IdUtenza`); gli istanti UTC finiscono con `Utc` (`CreatoIlUtc`, `AggiornatoIlUtc`); i booleani esprimono lo stato (`Primario`, `PaginaIniziale`).
- Indici, chiavi e vincoli seguono lo stesso schema (`PK_Utenze`, `IX_Utenze_IdTenant_EmailNormalizzata`).
- Metodi e funzioni: verbo italiano all'infinito o all'imperativo secondo il contesto (`CreaPagina`, `PubblicaRevisione`, `RisolviTenantDaHost`); interfacce con prefisso `I` (`IInvioEmail`, `ILocalizzatore`).
- Nel codice una nuova tabella o entità si aggiunge a questa pagina nello stesso PR.

| Entità (classe) | Tabella | Entità (classe) | Tabella |
|---|---|---|---|
| `Tenant` | `Tenant` (eccezione, vedi sotto) | `Prodotto` | `Prodotti` |
| `Sito` | `Siti` | `VarianteProdotto` | `VariantiProdotto` |
| `DominioSito` | `DominiSito` | `CategoriaProdotto` | `CategorieProdotto` |
| `ProfiloGuest` | `ProfiliGuest` | `CollezioneProdotto` | `CollezioniProdotto` |
| `Utenza` | `Utenze` | `Prezzo` | `Prezzi` |
| `ImpostazioniEmailTenant` | `ImpostazioniEmailTenant` | `ArticoloMagazzino` | `ArticoliMagazzino` |
| `RichiestaRecuperoPassword` | `RichiesteRecuperoPassword` | `MovimentoMagazzino` | `MovimentiMagazzino` |
| `Pagina` | `Pagine` | `Carrello` | `Carrelli` |
| `RevisionePagina` | `RevisioniPagina` | `RigaCarrello` | `RigheCarrello` |
| `Modello` | `Modelli` | `Ordine` | `Ordini` |
| `VersioneModello` | `VersioniModello` | `RigaOrdine` | `RigheOrdine` |
| `ImpostazioniTemaSito` | `ImpostazioniTemaSito` | `TentativoPagamento` | `TentativiPagamento` |
| `RisorsaMedia` | `RisorseMedia` | `Pagamento` | `Pagamenti` |
| `MessaggioOutbox` | `MessaggiOutbox` | `Rimborso` | `Rimborsi` |
| `RegistroAudit` | `RegistroAudit` | `Spedizione` | `Spedizioni` |
| `Reindirizzamento` | `Reindirizzamenti` | `EventoWebhook` | `EventiWebhook` |
| `Lingua` | `Lingue` | `TestoStatico` | `TestiStatici` |
| `LinguaSito` | `LingueSito` | `TraduzioneProdotto` | `TraduzioniProdotto` |

Note:

- La tabella degli account si chiama `Utenze` (e non `Utenti`) per non confondersi con il ruolo `Utente`, che è l'acquirente dell'e-commerce: `Utenze` contiene gli account di tutti i ruoli.
- `Tenant` mantiene il termine inglese, usato in tutto il progetto (`IdTenant`, `TenantAdmin`).
- Le entità di contenuto con testi modificabili dall'utente hanno una tabella di traduzioni, vedi "Lingue e localizzazione".

## Lingue e localizzazione

Le lingue iniziali sono **italiano** (lingua di base) e **inglese** (ADR-0018).

- `Lingua` (`Lingue`): `Id`, `Codice` (`it`, `en`), `Nome`, `Cultura` (`it-IT`, `en-GB`), `Predefinita` (italiano), `Attiva`. Globale alla piattaforma, inserita dalla migrazione (`it` e `en`).
- `LinguaSito` (`LingueSito`): `IdTenant`, `IdSito`, `IdLingua`, `Predefinita`, `CreatoIlUtc` (una lingua si disabilita togliendo la riga). Le lingue abilitate per ciascun sito e la lingua predefinita del sito.
- `TestoStatico` (`TestiStatici`): testi statici delle interfacce (HTML, messaggi, email). Globale alla piattaforma, **non** legata a un tenant: `Id`, `IdLingua`, `Chiave` (il testo statico in italiano così come scritto nella vista), `HashChiave` (SHA-256 della chiave, per l'indice), `Testo` (la traduzione nella lingua), `DaTradurre` (vero se il testo è stato creato in automatico con il testo italiano e non è ancora stato tradotto), `AggiornatoIlUtc`, `AggiornatoDaIdUtenza`. Indice univoco su `(IdLingua, HashChiave)`.
- Testi dinamici creati dall'utente:
  - le **pagine** hanno una riga `Pagina` per lingua (stesso `IdGruppoTraduzione`), con revisioni, SEO e slug propri;
  - le altre entità con testi editabili (prodotti, categorie, collezioni, testo alternativo dei media, ecc.) hanno una tabella di traduzioni `Traduzioni<Entità>` con chiave `(IdEntità, IdLingua)` e i campi di testo; se manca la traduzione per la lingua richiesta si usa la lingua predefinita del sito.

## Regole per gli ID e i vincoli

- PK surrogate `Guid`/UUID per identificativi pubblici e relazioni, salvo motivi contrari documentati.
- Includere `IdTenant` (e spesso `IdSito`) negli indici univoci e nelle FK composite dei dati tenant-scoped.
- Usare `decimal` per importi, sempre con valuta esplicita.
- Usare UTC per istanti; non confondere istante UTC e fuso orario del sito.
- Preferire enum/constraint o value objects per gli stati critici, non stringhe arbitrarie senza validazione.
- Valutare indici su `(IdSito, Slug, IdLingua)` e sulle colonne usate dai job e dalle query di catalogo.

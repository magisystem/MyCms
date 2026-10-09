# Modello di dominio e dati

Questo è il modello logico iniziale; i nomi e i tipi definitivi vanno tradotti in entity/configuration EF Core durante il bootstrap. Non è necessario che ogni riga diventi subito una tabella autonoma.

## Tenant, sito e dominio

- `Tenant`: cliente/organizzazione contrattuale della piattaforma. Il tenant primario (di gestione) ha `Primario` vero: ne esiste uno solo ed è creato dal seeding (ADR-0014).
- `Sito`: sito appartenente a un tenant. Un tenant può avere più siti.
- `DominioSito`: host registrato, normalizzato e associato a un sito, con `VerificatoIlUtc`, `VerificatoDaIdUtenza` e stato attivo/disattivo. La verifica è manuale (ADR-0014).
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
- `IdUltimaRevisioneBozza`, `IdRevisionePubblicata`
- `CreatoIlUtc`, `AggiornatoIlUtc`, `TokenConcorrenza` (`rowversion`)

Slug riservati: `admin` (area di amministrazione, `/admin`) e i percorsi degli asset statici (es. `lib`) non sono utilizzabili come slug di pagina e vanno rifiutati alla creazione. Ogni lingua di una pagina è una riga `Pagina` distinta con le proprie revisioni e il proprio slug; lo slug è univoco all'interno di sito e lingua (indice univoco su `(IdSito, IdLingua, Slug)`; `IdLingua` non è nullable, quindi non c'è ambiguità sui NULL). Cambiare slug di una pagina pubblicata deve passare da un caso d'uso che gestisce redirect/canonical.

### `RevisionePagina`

- `Id`, `IdTenant`, `IdSito`, `IdPagina`
- `NumeroRevisione`, `VersioneSchema`
- `ProgettoEditorJson` (stato serializzato per ricaricare il builder)
- `DocumentoRenderJson` (documento canonico che il renderer supporta)
- `SeoJson` o campi SEO strutturati
- `CreatoDaIdUtenza`, `CreatoIlUtc`, `NotaRevisione`

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

## Commerce

- `Prodotto`, `VarianteProdotto`, `CategoriaProdotto`, `CollezioneProdotto`
- `Prezzo` o prezzo per variante con valuta esplicita
- `ArticoloMagazzino` e `MovimentoMagazzino` (non trattare solo una quantità modificabile senza storico se serve audit)
- `Carrello`, `RigaCarrello`
- `Ordine`, `RigaOrdine` con snapshot di nome, SKU, prezzo, imposte/valori applicati e valuta
- `TentativoPagamento`/`Pagamento`, `Rimborso`, `Spedizione`
- `EventoWebhook` deduplicato e tracciabile

I confini definitivi di imposte, spedizioni, prenotazione giacenza, multi-valuta e resi dipendono dai requisiti commerciali, da definire prima del checkout di produzione.

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
- `LinguaSito` (`LingueSito`): `IdTenant`, `IdSito`, `IdLingua`, `Predefinita`, `Attiva`. Le lingue abilitate per ciascun sito e la lingua predefinita del sito.
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

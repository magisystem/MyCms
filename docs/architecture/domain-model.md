# Modello di dominio e dati

Questo è il modello logico iniziale; i nomi e i tipi definitivi vanno tradotti in entity/configuration EF Core durante il bootstrap. Non è necessario che ogni riga diventi subito una tabella autonoma.

## Tenant, sito e dominio

- `Tenant`: cliente/organizzazione contrattuale della piattaforma.
- `Site`: sito appartenente a un tenant. Un tenant può avere più siti.
- `SiteDomain`: host registrato, normalizzato e associato a un sito, con `VerifiedAtUtc`, `VerifiedByUserId` e stato attivo/disattivo. La verifica è manuale (ADR-0014).
- `User`: account (ASP.NET Core Identity). Gli account amministrativi sono globali con flag `IsPlatformAdmin`; gli account `Utente` (acquirenti) hanno `TenantId`.
- `TenantMembership`: membership di un account `TenantAdmin` nel proprio tenant (un solo tenant per account; vincolo univoco su `UserId`). Il `PlatformAdmin` non ha membership: accede a tutti i tenant.
- `TenantGuestProfile`: profilo del `Guest` del tenant, creato insieme al tenant, senza impostazioni: il Guest vede tutto il contenuto pubblicato.
- `Site.CustomerRegistrationEnabled`: abilita la registrazione degli acquirenti; vero solo se l'e-commerce è attivo (default falso).
- `PasswordRecoveryRequest`: richiesta di recupero con hash dell'OTP, tentativi, scadenza, hash del token del link monouso (emesso solo dopo l'OTP corretto) e stato.

Creare o elevare un `TenantAdmin` è riservato al `PlatformAdmin` (ADR-0011).

## CMS

### `Page`

- `Id`, `TenantId`, `SiteId`
- `Slug`, `PageKind`, `Status`
- `IsHomePage`, `Locale` (se multilingua abilitato)
- `LatestDraftRevisionId`, `PublishedRevisionId`
- `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken` (`rowversion`)

Slug univoco all'interno del sito e della lingua, quando applicabile. In SQL Server i NULL in un indice univoco sono considerati uguali: se `Locale` è nullable scegliere esplicitamente colonna non nullable, sentinella o indice filtrato. `Status` e `PublishedRevisionId` devono restare coerenti: definire gli stati (es. `Draft`, `Published`, `Unpublished`) e il caso d'uso di unpublish. Cambiare slug di una pagina pubblicata deve passare da un caso d'uso che gestisce redirect/canonical.

### `PageRevision`

- `Id`, `TenantId`, `SiteId`, `PageId`
- `RevisionNumber`, `SchemaVersion`
- `EditorProjectJson` (stato serializzato per ricaricare il builder)
- `RenderDocumentJson` (documento canonico che il renderer supporta)
- `SeoJson` o campi SEO strutturati
- `CreatedByUserId`, `CreatedAtUtc`, `RevisionNote`

Le revisioni sono immutabili dopo la creazione. Il puntatore `PublishedRevisionId` della pagina cambia in una transazione.

### Template

- `Template`: identità stabile del template, scope globale o specifico tema.
- `TemplateVersion`: versione immutabile di struttura iniziale, schema e policy di editing.
- `ThemeVersion`/registro tema: la versione del tema che il documento richiede o con cui è stato verificato.

I template non devono sovrascrivere contenuti già creati quando viene pubblicata una nuova versione.

## Temi

`SiteThemeSettings` (o configurazione equivalente): `TenantId`, `SiteId`, `ThemeKey`, `ThemeVersion`, `SettingsJson`, `UpdatedAtUtc`. Settings JSON deve essere validato contro lo schema dichiarato dal tema. Nessun percorso di file/assembly arbitrario proveniente dal database viene caricato come codice.

## Media

`MediaAsset`: ID, tenant, sito o scope, object key, nome originario, content type rilevato, dimensione, hash, dimensioni immagine, alt text, stato di scansione e timestamp. Il file binario sta nello storage, non in una colonna DB per default.

## Commerce

- `Product`, `ProductVariant`, `ProductCategory`, `ProductCollection`
- `Price` o prezzo per variante con valuta esplicita
- `InventoryItem` e `InventoryMovement` (non trattare solo una quantità modificabile senza storico se serve audit)
- `Cart`, `CartItem`
- `Order`, `OrderLine` con snapshot di nome, SKU, prezzo, imposte/valori applicati e valuta
- `PaymentAttempt`/`Payment`, `Refund`, `Shipment`
- `WebhookEvent` deduplicato e tracciabile

I confini definitivi di imposte, spedizioni, prenotazione giacenza, multi-valuta e resi dipendono dai requisiti commerciali, da definire prima del checkout di produzione.

## Tabelle operative

- `OutboxMessage`: evento registrato atomicamente con la transazione di dominio.
- `AuditLog`: attore, azione, risorsa, tenant, esito e timestamp; evitare segreti e payload sensibili.
- `Redirect`: sito, source path, destination, status code, data di creazione.

## Regole per gli ID e i vincoli

- PK surrogate `Guid`/UUID per identificativi pubblici e relazioni, salvo motivi contrari documentati.
- Includere `TenantId` (e spesso `SiteId`) negli indici univoci e nelle FK composite dei dati tenant-scoped.
- Usare `decimal` per importi, sempre con valuta esplicita.
- Usare UTC per istanti; non confondere istante UTC e fuso orario del sito.
- Preferire enum/constraint o value objects per gli stati critici, non stringhe arbitrarie senza validazione.
- Valutare indici su `(SiteId, Slug, Locale)` e sulle colonne usate dai job e dalle query di catalogo.

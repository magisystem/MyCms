# Temi e rendering Razor

## Principi

1. Il tema definisce presentazione, layout, CSS/JS e renderer ammessi.
2. Il template definisce una struttura iniziale e una policy di editing.
3. Il documento di pagina contiene i contenuti e la composizione.
4. I dati dinamici (es. prodotti) restano nei rispettivi moduli di dominio e non vengono duplicati nel JSON della pagina.
5. Le viste Razor e gli assembly sono codice fidato, distribuito con la piattaforma.

## Packaging

Per i temi interni usare Razor Class Libraries o una struttura di viste Razor controllata, versione e asset statici. Il registro dei temi espone almeno `ChiaveTema`, `VersioneTema`, schema settings, tipi di componente supportati e mapping dei renderer.

Non selezionare view path o assembly tramite stringhe non attendibili salvate dal tenant. Il tema attivo deve essere una chiave risolta da un registry allow-list.

## Contratto concettuale

```csharp
public interface IDefinizioneTema
{
    string Chiave { get; }
    string Versione { get; }
    IReadOnlyCollection<string> ComponentiSupportati { get; }
    SchemaImpostazioniTema SchemaImpostazioni { get; }
}
```

È un esempio illustrativo, non un contratto definitivo. Evitare che il tema acceda direttamente al `DbContext`: riceve dati view-model già caricati e autorizzati dall'application layer.

## Rendering

- Layout globale: header, navigation, footer, asset e metadata condivisi.
- Page renderer: valida/legge il documento CMS e delega ogni `TipoComponente` a un renderer registrato.
- Component renderer: genera HTML server-side con Razor/Tag Helper/partial o View Component, a seconda della complessità.
- Commerce component: chiama servizi del modulo Store e non si fida dei dati forniti dal browser.

Il mapping tra `TipoComponente` e renderer deve essere esplicito. Non eseguire nomi di classi, percorsi Razor, espressioni C# o script contenuti nei documenti.

## Versioni e compatibilità

- Ogni tema ha versione semantica o schema versione equivalente.
- Ogni documento di render ha `versioneSchema`.
- Ogni template ha versione immutabile.
- Dichiarare le versioni del documento/componenti supportate dal tema.
- Non aggiornare automaticamente tutti i tenant a una nuova versione del tema senza piano di compatibilità e test.
- Un cambio di schema richiede migrazione esplicita e test di round-trip.

## CSS e asset

Preferire token CSS dichiarativi e asset versionati. Generare nomi asset content-hashed dove la pipeline lo permette. Gli asset del tema devono essere pubblicabili senza permettere al tenant di indicare URL o percorsi arbitrari per asset interni.

## Comportamento in errore

Se un documento contiene un componente non supportato, non eseguire fallback arbitrari. In preview/editor mostrare un errore diagnostico. Sul sito live usare un fallback controllato, logging e una strategia di rollback della revisione; non esporre stack trace o JSON interno al visitatore.

## Attuazione (D-001, D-002)

- **Registro (allow-list):** `RegistroTemi` (Application) contiene le versioni rilasciate (`classico` v1, `moderno` v1). Una definizione è chiave, versione e schema delle impostazioni; la vista e il foglio di stile di ogni versione sono associati in codice (`TemiVista`), mai letti da dati. Il registro rifiuta chiavi non valide (solo `[a-z][a-z0-9-]*`), versioni duplicate, schemi malformati e l'assenza del tema predefinito; un test fissa l'impronta SHA-256 di ogni versione rilasciata (come per i modelli di pagina): per cambiare un tema si aggiunge una versione nuova.
- **Impostazioni per sito:** tabella `ImpostazioniTemaSito` (una riga per sito, chiave esterna composta `(IdTenant, IdSito)`, `ChiaveTema`, `VersioneTema`, `ImpostazioniJson`, token di concorrenza). Nessuna riga = tema predefinito. Il valore salvato è sempre una chiave del registro: non può essere un percorso, un tipo o un assembly.
- **Schema e validazione:** tipi di campo `Colore` (`#RRGGBB`), `Testo` (lunghezza massima, senza caratteri di controllo) e `Scelta` (valori ammessi). Il server valida a ogni salvataggio (campi non previsti rifiutati, errori per nome del campo, mai i valori), salva in forma normalizzata (colori minuscoli, vuoti e predefiniti omessi) e in lettura ignora in silenzio un valore non più valido: il sito pubblico non si rompe per un'impostazione.
- **Versione fissata:** scegliendo un tema nuovo si usa la sua ultima versione; restando sullo stesso tema la versione configurata non cambia da sola. Un tema o una versione che non sono più nel registro ripiegano sul tema predefinito con un avviso nei log.
- **Rendering:** il documento pubblico (`_DocumentoPubblico`) carica il foglio del tema e delega corpo della pagina al partial del tema (`Pages/Shared/Temi/_{chiave}_v{versione}`), che compone intestazione con il nome del sito, selettore di lingua (`_SelettoreLingua`), `<main>` con il partial del modello di pagina e piè di pagina. I colori arrivano al CSS come variabili `--tema-*` scritte solo se superano di nuovo la validazione `#RRGGBB`; testi e nome del sito sono sempre codificati da Razor. Il contenuto resta nelle revisioni e non dipende dal tema: due temi disegnano la stessa pagina senza duplicarla. La pagina 404 e le anteprime usano lo stesso tema (le anteprime restano `noindex`).
- **Admin:** `/admin/tema` (voce "Aspetto"): scelta del tema, campi dello schema scelto, token di concorrenza. Solo `TenantAdmin` del tenant o `PlatformAdmin`, riverificati sul database a ogni operazione; la modifica è registrata nell'audit (`ModificaTema`).
- **Temi di prova:** `classico` (filetto e link del colore primario, piè di pagina opzionale) e `moderno` (banda scura per intestazione e piè di pagina, titoli del colore primario, larghezza del contenuto stretta o larga, selettore di lingua nell'intestazione).


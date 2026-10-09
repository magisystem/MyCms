# Temi e rendering Razor

## Principi

1. Il tema definisce presentazione, layout, CSS/JS e renderer ammessi.
2. Il template definisce una struttura iniziale e una policy di editing.
3. Il documento di pagina contiene i contenuti e la composizione.
4. I dati dinamici (es. prodotti) restano nei rispettivi moduli di dominio e non vengono duplicati nel JSON della pagina.
5. Le viste Razor e gli assembly sono codice fidato, distribuito con la piattaforma.

## Packaging

Per i temi interni usare Razor Class Libraries o una struttura di viste Razor controllata, versione e asset statici. Il registro dei temi espone almeno `ThemeKey`, `ThemeVersion`, schema settings, tipi di componente supportati e mapping dei renderer.

Non selezionare view path o assembly tramite stringhe non attendibili salvate dal tenant. Il tema attivo deve essere una chiave risolta da un registry allow-list.

## Contratto concettuale

```csharp
public interface IThemeDefinition
{
    string Key { get; }
    string Version { get; }
    IReadOnlyCollection<string> SupportedComponents { get; }
    ThemeSettingsSchema SettingsSchema { get; }
}
```

È un esempio illustrativo, non un contratto definitivo. Evitare che il tema acceda direttamente al `DbContext`: riceve dati view-model già caricati e autorizzati dall'application layer.

## Rendering

- Layout globale: header, navigation, footer, asset e metadata condivisi.
- Page renderer: valida/legge il documento CMS e delega ogni `ComponentType` a un renderer registrato.
- Component renderer: genera HTML server-side con Razor/Tag Helper/partial o View Component, a seconda della complessità.
- Commerce component: chiama servizi del modulo Store e non si fida dei dati forniti dal browser.

Il mapping tra `ComponentType` e renderer deve essere esplicito. Non eseguire nomi di classi, percorsi Razor, espressioni C# o script contenuti nei documenti.

## Versioni e compatibilità

- Ogni tema ha versione semantica o schema versione equivalente.
- Ogni render document ha `schemaVersion`.
- Ogni template ha versione immutabile.
- Dichiarare le versioni del documento/componenti supportate dal tema.
- Non aggiornare automaticamente tutti i tenant a una nuova versione del tema senza piano di compatibilità e test.
- Un cambio di schema richiede migrazione esplicita e test di round-trip.

## CSS e asset

Preferire token CSS dichiarativi e asset versionati. Generare nomi asset content-hashed dove la pipeline lo permette. Gli asset del tema devono essere pubblicabili senza permettere al tenant di indicare URL o percorsi arbitrari per asset interni.

## Comportamento in errore

Se un documento contiene un componente non supportato, non eseguire fallback arbitrari. In preview/editor mostrare un errore diagnostico. Sul sito live usare un fallback controllato, logging e una strategia di rollback della revisione; non esporre stack trace o JSON interno al visitatore.

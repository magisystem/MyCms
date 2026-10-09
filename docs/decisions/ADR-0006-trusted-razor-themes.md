# ADR-0006 — Temi Razor fidati, compilati e versionati

- **Stato:** Accepted
- **Data:** 2026-10-09

## Contesto

Ogni tenant può avere identità grafica dedicata, ma eseguire viste o assembly caricati da clienti comprometterebbe sicurezza e stabilità del processo condiviso.

## Decisione

I temi sono Razor Class Libraries o pacchetti di viste/asset controllati dal team, versionati e distribuiti con il deployment. Il tenant seleziona un tema da un registry allow-list e configura solo settings validati contro uno schema. Non carica `.cshtml`, C#, DLL o JS arbitrario.

## Alternative considerate

- Temi server-side caricabili dinamicamente dal tenant: rifiutati per rischio di esecuzione di codice e complessità deployment.
- Un singolo tema fisso: non soddisfa l'esigenza di clienti con identità diverse.

## Conseguenze

Nuovi temi richiedono processo di release e test. La compatibilità tra tema, componenti e documenti deve essere dichiarata. Settings e contenuto rimangono dati separati dal codice.

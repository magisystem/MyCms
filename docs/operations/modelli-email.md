# Modelli email — esercizio

Riferimento: [ADR-0025](../decisions/ADR-0025-modelli-email.md). Pagine: `/admin/email/modelli` (solo `TenantAdmin`).

## Cosa può fare il TenantAdmin

- Vedere l'elenco dei modelli e, per ogni lingua del sito, se usa il modello di fabbrica o uno suo (pulsante pieno = personalizzato).
- Modificare oggetto, corpo HTML e alternativa testo; vedere l'anteprima con dati d'esempio; salvare; ripristinare il modello di fabbrica.
- I modelli **critici** (codice di recupero, link di recupero, conferma registrazione, invito) devono riportare `{{ codice }}` o `{{ link }}` sia nell'HTML sia nel testo, altrimenti non si salvano.

## Quale modello si usa

1. Il modello del tenant nella lingua del destinatario.
2. Il modello del tenant nella lingua predefinita del tenant.
3. Il modello di fabbrica nella lingua del destinatario (italiano e inglese).
4. Il modello di fabbrica italiano.

Se un modello del tenant non si lascia più rendere (ad esempio dopo un aggiornamento che toglie una variabile), l'email parte con quello di fabbrica e nel registro compare l'avviso «Il modello email del tenant non si lascia rendere, si usa quello di fabbrica».

## Controlli e sospetti

- **«Il modello non è valido»**: la pagina elenca gli errori (sintassi, variabile sconosciuta, variabile obbligatoria mancante, elementi non ammessi nell'HTML).
- **Email con il testo di fabbrica nonostante un modello salvato**: guardare il registro per l'avviso sopra; verificare la lingua del destinatario e che il modello sia salvato proprio in quella lingua o nella predefinita.
- **Cronologia**: `ModificaModelloEmail` e `RipristinoModelloEmail` nel registro di audit (chi, quando, quale modello; mai il contenuto).
- **Ripristino della piattaforma**: i modelli sono dati del tenant nella tabella `ModelliEmail` e seguono backup e ripristino del database; i modelli di fabbrica sono nel codice.

## Per chi sviluppa

Aggiungere un modello: nuovo codice in `CodiciModelloEmail`, definizione e dati d'esempio in `CatalogoModelliEmail`, modelli di fabbrica in `ModelliDiFabbrica` (italiano e inglese), poi comporre con `IComponitoreEmail.ComponiAsync`. Il test `ModelliEmailTest` verifica che ogni codice abbia i due modelli di fabbrica e che si rendano con i dati d'esempio.

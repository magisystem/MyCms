# ADR-0022 — Nexi XPay come secondo provider di pagamento

- **Stato:** Accepted (richiesta del committente del 2026-10-13)
- **Data:** 2026-10-13
- **Modifica:** ADR-0020, voce 1 (provider unico PayPal): il negozio ammette più provider; il cliente sceglie alla cassa.

## Contesto

ADR-0020 sceglie PayPal e lascia aperto un secondo provider («si aggiunge senza toccare il dominio»). Il committente chiede **Nexi** (molto usato in Italia) e decide che i **rimborsi si fanno a mano** dal pannello del provider, non dal nostro. Specifica usata: portale sviluppatori Nexi, *XPay* (API `payment-api-v1`, notifiche, ambiente di test); lettura del 2026-10-13.

## Decisione

- **Integrazione con la pagina di pagamento ospitata (HPP)**: `POST /orders/hpp` crea il pagamento e restituisce `hostedPage` (dove mandare il cliente) e `securityToken`; la carta e la 3-D Secure si fanno su Nexi, nessun dato di carta passa da noi. Nessun SDK, `HttpClient` diretto (come ADR-0015, ADR-0020). Indirizzi: test `https://xpaysandbox.nexigroup.com/api/phoenix-0.0/psp/api/v1/`, produzione `https://xpay.nexigroup.com/api/phoenix-0.0/psp/api/v1/`; intestazioni `X-API-KEY` (chiave del terminale) e `Correlation-Id` (UUID).
- **Importo e valuta li decide il server**: l'importo dell'ordine, in centesimi (stringa), solo EUR. `captureType` **IMPLICIT** (incasso automatico), `actionType` `PAY`, lingua dal sito (`ita`, `eng`…), `resultUrl` = pagina di ritorno dell'ordine, `cancelUrl` = dettaglio dell'ordine, `notificationUrl` = `https://<dominio del sito>/webhooks/nexi`.
- **Identificativo ordine presso Nexi** (`orderId`, al più 27 caratteri, alfabeto ristretto): `NX<numero ordine>-<10 caratteri del tentativo>`. Ogni tentativo di pagamento ne ha uno nuovo: un ordine fallito si ripaga con un nuovo tentativo.
- **Esito sempre riletto dal gateway** (`GET /orders/{orderId}`), mai dal browser né dal contenuto di una notifica. Pagato solo con un'operazione `CAPTURE` con risultato `EXECUTED` e **importo e valuta uguali a quelli dell'ordine** (altrimenti «da rimborsare»); `PENDING` o un'autorizzazione non ancora incassata = in sospeso; `DECLINED`, `DENIED_BY_RISK`, `THREEDS_FAILED`, `FAILED` = rifiutato; ordine inesistente, `CANCELED`, 3DS non completato = non approvato (nulla incassato). Tutti i passaggi di stato restano aggiornamenti condizionali e idempotenti (ritorno del cliente, notifica e riconciliazione possono arrivare insieme, ripetuti o in disordine).
- **Notifiche** (`POST /webhooks/nexi`, pubblico, sul dominio del sito): il gateway non le firma ma ripete il `securityToken` ricevuto alla creazione. Del token si conserva solo l'**impronta SHA-256** (`TentativiPagamento.HashTokenNotifica`) e la si confronta in tempo costante; senza token valido nessun effetto (401). Deduplica per `eventId` (`EventiWebhook`, provider `Nexi`). Nexi **non ripete** le notifiche perse: il ritorno del cliente e la **riconciliazione** periodica (che ora vale per tutti i provider) coprono la mancata consegna. Gli indirizzi IP da cui Nexi invia sono nella sua documentazione e non si usano come controllo (cambiano e non sono un segreto).
- **Credenziali per sito** (`ImpostazioniNexiSito`: ambiente + chiave API cifrata, in sola scrittura), gestite dal `PlatformAdmin` nella stessa pagina «Pagamenti» del tenant; il checkout di produzione resta dietro l'interruttore `Negozio:CheckoutProduzione`, che vale per ogni provider.
- **Cassa a scelta del cliente**: contratto comune `IGatewayPagamento` (`GatewayPayPal`, `GatewayNexi`); il tentativo salva il provider e il servizio dei pagamenti lo usa per incassare, leggere e riconciliare. La pagina dell'ordine mostra un pulsante per ogni provider configurato per il sito (e pronto: produzione con interruttore acceso); un sito senza provider mostra un avviso.
- **Nessun rimborso dal pannello per Nexi**: rimborsi e restituzione di incassi «orfani» si fanno dal back office Nexi; il dettaglio ordine lo dice e non offre i pulsanti. Il rimborso fatto fuori dal nostro sistema (per PayPal come per Nexi) non cambia oggi lo stato dell'ordine: va annotato dal tenant (vedi «Aperto»).

## Alternative considerate

- **XPay Build** (campi carta in iframe nel nostro sito): più integrata, ma richiede più superficie (script di terzi e CSP più larga) e non serve a questo livello.
- **Pay-by-Link**: pensata per invii fuori dal sito.
- **Dati di carta lato server** (Server to Server): richiede PCI DSS SAQ D, respinto.
- **Sostituire PayPal**: respinto, il cliente sceglie.

## Conseguenze

- **Da verificare con il Sandbox Nexi** (a cura del committente, con le credenziali del suo terminale; le chiavi pubbliche di prova stanno nella documentazione Nexi): dominio effettivo della pagina ospitata (oggi si accetta qualunque HTTPS su nome di dominio, mai IP/porta/credenziali), forma esatta delle operazioni restituite da `GET /orders/{orderId}` con l'incasso implicito, arrivo delle notifiche. La suite usa un Nexi finto.
- **Aperto:** riconoscere i rimborsi fatti a mano dai pannelli PayPal e Nexi (notifica `REFUND` / webhook di rimborso non richiesto da noi) e segnare l'ordine come rimborsato; oggi vengono ignorati.

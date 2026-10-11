# Contratto fra componenti e temi

Stato: attuato in M5. Fonte di verità del codice: `TokenComponenti` (Application/Builder). Un test (`ContrattoComponentiTemiTest`) tiene allineati catalogo, `componenti.css` e skin.

## Problema

I temi rilasciati sono immutabili (D-001): una versione di tema non si modifica. I componenti del page builder (sezione, hero, pulsante, …) hanno però un aspetto che un tema deve poter ritoccare (colori, raggi, larghezza) senza duplicare i fogli di stile dei componenti e senza che un nuovo componente rompa i temi esistenti.

## Regole

1. **Markup indipendente dal tema.** Il markup dei componenti (`Componenti/_Nodo`) e le sue classi `dv-*` sono uguali con ogni tema.
2. **Solo token.** `wwwroot/css/componenti.css` legge esclusivamente proprietà personalizzate `--dv-*` del catalogo `TokenComponenti`, sempre nella forma `var(--dv-x, predefinito)`: il valore di riserva è il predefinito del catalogo. Senza alcuna skin i componenti si vedono con i predefiniti. `componenti.css` non dichiara mai i token.
3. **Skin del tema.** Un tema può fornire un foglio facoltativo `wwwroot/css/temi/<chiave>-componenti.css` che imposta **solo** token del catalogo, con un unico selettore `body.tema-<chiave>`. Si carica dopo il foglio del tema, solo nelle pagine del builder. L'associazione è in codice (`TemiVista.FoglioComponenti`), come per la vista e il foglio del tema: non viene mai da dati del tenant.
4. **Immutabilità.** Come i fogli dei temi, una skin rilasciata non cambia il significato dei token. Nuovo aspetto = nuova versione di tema (con la sua skin) o nuovo token.
5. **Nuovo token o nuovo componente.** Si aggiunge il token al catalogo (con descrizione e predefinito), si usa in `componenti.css` con riserva uguale al predefinito e lo si documenta qui. Un token esistente non cambia predefinito né significato: i temi esistenti non devono cambiare aspetto. Un token mai letto da `componenti.css` è un errore del test.
6. **Il tenant non scrive CSS.** I valori variabili per sito restano le impostazioni tipizzate del tema (`--tema-*`, validate dallo schema); le skin sono codice del repository, revisionato.

## Catalogo dei token

| Token | Uso | Predefinito |
|---|---|---|
| `--dv-larghezza-contenuto` | Larghezza massima del contenuto di una sezione. | `46rem` |
| `--dv-spazio-compatta` | Spazio verticale di una sezione con spaziatura compatta. | `1rem` |
| `--dv-spazio-normale` | Spazio verticale di una sezione con spaziatura normale. | `2rem` |
| `--dv-spazio-ampia` | Spazio verticale di una sezione con spaziatura ampia. | `4rem` |
| `--dv-colore-primario` | Colore di accento: sfondo «primario», pulsanti, bordo del pulsante secondario, filetto delle testimonianze, evidenziazione del focus. | `var(--tema-colore-primario, #1f4e8c)` |
| `--dv-colore-sfondo-scuro` | Sfondo delle sezioni, degli hero e degli inviti all'azione «scuri». | `var(--tema-colore-intestazione, #111827)` |
| `--dv-colore-testo-su-scuro` | Colore del testo sugli sfondi scuri e primari. | `#ffffff` |
| `--dv-colore-sfondo-chiaro` | Sfondo delle sezioni «chiare». | `#f1f5f9` |
| `--dv-colore-testo-su-chiaro` | Colore del testo sugli sfondi chiari. | `#1f2933` |
| `--dv-colore-sfondo-chiaro-notte` | Sfondo delle sezioni «chiare» con preferenza per la modalità scura del sistema. | `#1f2933` |
| `--dv-colore-testo-su-chiaro-notte` | Testo sugli sfondi chiari con preferenza per la modalità scura. | `#e4e7eb` |
| `--dv-colore-titoli` | Colore dei titoli (anche delle caratteristiche) fuori dagli sfondi scuri. | `inherit` |
| `--dv-colore-bordo` | Colore dei filetti (fisarmonica, schede delle caratteristiche). | `#cbd2d9` |
| `--dv-colore-didascalia` | Colore delle didascalie delle immagini e dell'autore delle testimonianze. | `#52606d` |
| `--dv-raggio-pulsante` | Raggio degli angoli dei pulsanti. | `0.4rem` |
| `--dv-durata-animazione` | Durata delle animazioni di comparsa. | `0.6s` |

## Editor

Il canvas del builder carica gli stessi fogli e nello stesso ordine della pagina pubblica (`pubblico.css`, `componenti.css`, foglio del tema, skin, poi gli stili solo-editor): il server indica quali (`data-tema` della pagina del builder), il browser non sceglie nulla. Classe e variabili del tema sono applicate al corpo del canvas e non entrano nel progetto, quindi non diventano contenuto della pagina.

## Skin esistenti

- `classico`: lo sfondo «scuro» usa il colore primario del sito.
- `moderno`: titoli nel colore primario, pulsanti a pillola, contenuto più largo (64rem).

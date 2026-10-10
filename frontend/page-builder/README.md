# Page builder (GrapesJS) — bundle isolato

Sorgenti del bundle JavaScript dell'editor visuale, usato **solo** dalla pagina `/admin/pagine/{id}/builder` (ADR-0007). L'admin
non è una SPA e il backend non usa Node: Node serve unicamente a ricompilare questo bundle. Il risultato compilato è **versionato**
in `src/DvCms.Web/wwwroot/lib/page-builder/` (in produzione non serve alcuna toolchain).

## Struttura

| File | Ruolo |
|---|---|
| `src/main.js` | Avvio: legge la configurazione dagli attributi `data-*` di `#builder`, carica lo stato dal server, monta GrapesJS |
| `src/catalogo.js` | Tipi di componente e blocchi, generati dal catalogo che il server serve (stessa fonte di `RegistroComponenti`) |
| `src/vincoli.js` | Vincoli dei template applicati all'interfaccia (nodi bloccati, slot). Solo guida: il server riverifica tutto |
| `src/salvataggio.js` | Salvataggio con token di concorrenza e gestione degli errori restituiti dal server |
| `src/interfaccia.js` | Scheletro dell'interfaccia (solo API del DOM, nessun `innerHTML`) |
| `src/builder.css`, `src/canvas.css` | Stili dell'interfaccia e del canvas |
| `build.mjs` | Compilazione con esbuild; `--verifica` confronta con i file versionati |
| `smoke/percorso-completo.mjs` | Verifica con un browser vero (manuale, non in CI): montaggio, vincoli, proprietà, salvataggio, anteprima per dispositivo, modifica nel canvas, salvataggio automatico, manomissione, CSP |

## Comandi

```bash
cd frontend/page-builder
npm ci                  # installa esattamente ciò che dice package-lock.json
npm run build           # rigenera src/DvCms.Web/wwwroot/lib/page-builder/ (poi versionare il risultato)
npm run verifica        # esce con 1 se il bundle versionato non corrisponde a sorgenti e lockfile (lo fa la CI)
npm audit               # vulnerabilità delle dipendenze (la CI fallisce da "high" in su)
```

Verifica con un browser vero (serve un'istanza in esecuzione con un `PlatformAdmin` e Chromium; il pacchetto `playwright` può essere
globale, `PLAYWRIGHT_MODULE` ne indica il percorso):

```bash
DVCMS_PASSWORD='…' CHROMIUM_PATH=/percorso/chromium node smoke/percorso-completo.mjs
```

## Dipendenze e licenze (regola 11 di `AGENTS.md`)

Verificate il 2026-10-10. `npm audit`: nessuna vulnerabilità. L'elenco delle licenze incluse nel bundle è generato in
`wwwroot/lib/page-builder/LICENZE-TERZE.txt` a ogni build.

| Pacchetto | Licenza | Note |
|---|---|---|
| `grapesjs` 0.23.6 | BSD-3-Clause | Motore dell'editor. Permette l'uso commerciale; va conservato l'avviso di copyright (riportato in `LICENZE-TERZE.txt`) |
| `backbone`, `underscore`, `codemirror`, `codemirror-formatting`, `html-entities`, `promise-polyfill` | MIT | Dipendenze dirette di GrapesJS |
| `backbone-undo` 0.2.6 | MIT | **Segnalato come non più supportato** da npm. Non ha vulnerabilità note; è bloccato dal lockfile. Rischio accettato: se GrapesJS non lo sostituisce, si valuta il fork o l'alternativa prima di M5 |
| `esbuild` 0.28.2 | MIT | Solo in sviluppo (compilazione), non entra nel bundle |

Note di sicurezza del bundle: contiene `new Function` (templating di underscore). Nei percorsi usati dal builder non viene eseguito e
la CSP della pagina (`script-src 'self'`, senza `unsafe-eval`) lo bloccherebbe comunque: il test di fumo controlla che non compaiano
violazioni. I pannelli di GrapesJS che richiederebbero risorse esterne o `eval` (codice, asset manager, rich text) non sono montati.
L'impostazione `cssIcons` è svuotata: di default GrapesJS caricherebbe Font Awesome da un CDN.

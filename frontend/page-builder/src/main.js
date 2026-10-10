// Punto d'ingresso del page builder (ADR-0007). Gira solo in /admin/pagine/{id}/builder: legge la configurazione dagli attributi
// data-* dell'elemento #builder (la pagina non contiene script), carica lo stato dal server, monta GrapesJS e salva con fetch.
// Il browser non è un'autorità: i vincoli dei template che applica qui (nodi bloccati, slot) servono solo a guidare l'interfaccia,
// il server li riverifica a ogni salvataggio.
import 'grapesjs/dist/css/grapes.min.css';
import './builder.css';
import grapesjs from 'grapesjs';
import { registraComponenti, creaBlocchi } from './catalogo.js';
import { applicaVincoli, regolaRadice } from './vincoli.js';
import { creaInterfaccia } from './interfaccia.js';
import { Salvataggio } from './salvataggio.js';

async function avvia() {
  const radice = document.getElementById('builder');
  if (!radice) return;

  const config = {
    urlStato: radice.dataset.urlStato,
    urlSalva: radice.dataset.urlSalva,
    urlPagina: radice.dataset.urlPagina,
    urlAnteprima: radice.dataset.urlAnteprima,
    risorse: JSON.parse(radice.dataset.risorse || '[]'),
    testi: JSON.parse(radice.dataset.testi || '{}'),
    tokenAntiforgery: document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '',
  };
  config.testi.componenti = JSON.parse(config.testi.componenti || '{}');
  config.testi.campi = JSON.parse(config.testi.campi || '{}');

  const ui = creaInterfaccia(radice, config);
  let stato;
  try {
    const risposta = await fetch(config.urlStato, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    if (!risposta.ok) throw new Error(String(risposta.status));
    stato = await risposta.json();
  } catch {
    ui.mostraStato(config.testi.erroreRete, 'errore');
    return;
  }

  ui.impostaTitolo(stato.titolo);
  const definizioni = new Map(stato.catalogo.map((c) => [c.tipo, c]));

  const editor = grapesjs.init({
    container: ui.contenitoreCanvas,
    height: '100%',
    width: 'auto',
    fromElement: false,
    storageManager: false,
    // Nessuna risorsa esterna: l'impostazione predefinita carica Font Awesome da un CDN (che la CSP bloccherebbe); le icone sono in builder.css.
    cssIcons: '',
    projectData: stato.progetto,
    avoidInlineStyle: true,
    panels: { defaults: [] },
    deviceManager: { devices: [] },
    styleManager: { sectors: [] },
    selectorManager: { componentFirst: true },
    blockManager: { appendTo: ui.contenitoreBlocchi },
    layerManager: { appendTo: ui.contenitoreLivelli },
    traitManager: { appendTo: ui.contenitoreProprieta },
    undoManager: { trackSelection: false },
    canvas: { styles: ['/css/pubblico.css', '/css/componenti.css', '/lib/page-builder/canvas.css'] },
  });

  registraComponenti(editor, stato.catalogo, stato.vincoli, config);
  creaBlocchi(editor, definizioni, config);

  const salvataggio = new Salvataggio(editor, config, ui, stato.token);
  editor.on('load', () => {
    // Il contenitore principale viene ricreato dal progetto caricato: le regole vanno applicate dopo il caricamento.
    regolaRadice(editor, stato.vincoli, definizioni);
    applicaVincoli(editor, stato.vincoli, definizioni, config);
    salvataggio.segnaPulito();
    window.dvEditor = editor; // per le verifiche automatiche del browser (nessun effetto sul server)
  });
  editor.on('component:add', (componente) => {
    if (definizioni.has(componente.get('type')) && !componente.getAttributes().id) {
      componente.addAttributes({ id: componente.getId() });
    }
  });
  editor.on('update', () => salvataggio.segnaModificato());
  ui.pulsanteSalva.addEventListener('click', () => salvataggio.salva());
  document.addEventListener('keydown', (evento) => {
    if ((evento.ctrlKey || evento.metaKey) && evento.key.toLowerCase() === 's') {
      evento.preventDefault();
      salvataggio.salva();
    }
  });
  window.addEventListener('beforeunload', (evento) => {
    if (salvataggio.modificato) {
      evento.preventDefault();
    }
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', avvia);
} else {
  avvia();
}

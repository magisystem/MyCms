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
import { migliora } from './proprieta.js';
import { creaAzioni, creaStruttura, nomeComponente, nomiCampiProprieta, rendiBlocchiAccessibili } from './tastiera.js';

async function avvia() {
  const radice = document.getElementById('builder');
  if (!radice) return;

  const config = {
    urlStato: radice.dataset.urlStato,
    urlSalva: radice.dataset.urlSalva,
    urlPagina: radice.dataset.urlPagina,
    urlAnteprima: radice.dataset.urlAnteprima,
    risorse: JSON.parse(radice.dataset.risorse || '[]'),
    prodotti: JSON.parse(radice.dataset.prodotti || '[]'),
    categorie: JSON.parse(radice.dataset.categorie || '[]'),
    moduli: JSON.parse(radice.dataset.moduli || '[]'),
    testi: JSON.parse(radice.dataset.testi || '{}'),
    tema: JSON.parse(radice.dataset.tema || '{}'),
    tokenAntiforgery: document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '',
  };
  config.testi.componenti = JSON.parse(config.testi.componenti || '{}');
  config.testi.campi = JSON.parse(config.testi.campi || '{}');
  config.testi.gruppi = JSON.parse(config.testi.gruppi || '{}');

  const ui = creaInterfaccia(radice, config);
  ui.creaDispositivi(config.testi, (id) => window.dvEditor?.setDevice(id));
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
    deviceManager: {
      devices: [
        { id: 'desktop', name: config.testi.desktop ?? 'Desktop', width: '' },
        { id: 'tablet', name: config.testi.tablet ?? 'Tablet', width: '768px', widthMedia: '992px' },
        { id: 'mobile', name: config.testi.mobile ?? 'Mobile', width: '375px', widthMedia: '480px' },
      ],
    },
    styleManager: { sectors: [] },
    selectorManager: { componentFirst: true },
    // appendOnClick: un clic (o Invio/Spazio sul blocco) inserisce il blocco dove i vincoli lo consentono: è l'alternativa al trascinamento.
    blockManager: { appendTo: ui.contenitoreBlocchi, appendOnClick: true },
    // Il trascinamento dei blocchi di GrapesJS richiede il Layer Manager disegnato: resta montato fuori dalla pagina; l'albero
    // accessibile "Struttura" (tastiera.js) è quello che si usa e si vede.
    layerManager: { appendTo: document.createElement('div') },
    traitManager: { appendTo: ui.contenitoreProprieta },
    undoManager: { trackSelection: false },
    // Stessi fogli e stesso ordine della pagina pubblica (tema e skin dei componenti indicati dal server), poi gli stili solo-editor.
    canvas: { styles: ['/css/pubblico.css', '/css/componenti.css', ...(config.tema.fogli ?? []), '/lib/page-builder/canvas.css'] },
  });

  // Niente scorciatoie che agiscono sui componenti (eliminare, spostarsi, copiare): scrivendo nel canvas un tasto non deve mai
  // cancellare il componente selezionato. Restano annulla/ripeti; eliminare e duplicare si fa dalla barra del componente.
  for (const id of ['core:component-delete', 'core:component-next', 'core:component-prev', 'core:component-enter', 'core:component-exit', 'core:copy', 'core:paste']) {
    editor.Keymaps.remove(id);
  }

  registraComponenti(editor, stato.catalogo, stato.vincoli, config);
  creaBlocchi(editor, definizioni, config);

  // Uso da tastiera e con lo screen reader (alternative al mouse, fuori dal canvas).
  const contestoTastiera = { testi: config.testi, definizioni, vincoli: stato.vincoli, annuncia: ui.annuncia, vaiAProprieta: ui.vaiAProprieta };
  rendiBlocchiAccessibili(ui.contenitoreBlocchi, config.testi);
  nomiCampiProprieta(ui.contenitoreProprieta);
  migliora(ui.contenitoreProprieta, editor, config.testi);
  creaStruttura(editor, ui.contenitoreStruttura, contestoTastiera);
  creaAzioni(editor, ui.contenitoreAzioni, contestoTastiera);
  ui.pulsanteAnnulla.addEventListener('click', () => editor.UndoManager.undo());
  ui.pulsanteRipeti.addEventListener('click', () => editor.UndoManager.redo());
  const aggiornaStorico = () => {
    ui.pulsanteAnnulla.disabled = !editor.UndoManager.hasUndo();
    ui.pulsanteRipeti.disabled = !editor.UndoManager.hasRedo();
  };
  editor.on('update undo redo load', aggiornaStorico);
  aggiornaStorico();
  editor.on('component:selected', (componente) => {
    if (definizioni.has(componente.get('type'))) ui.annuncia((config.testi.selezionato ?? '{0}').split('{0}').join(nomeComponente(componente)));
  });
  editor.on('canvas:frame:load:body', () => {
    // Il canvas non si usa da tastiera: il titolo dell'iframe dice dove andare.
    const cornice = ui.contenitoreCanvas.querySelector('iframe');
    if (cornice) cornice.title = config.testi.anteprimaPagina ?? '';
  });

  const salvataggio = new Salvataggio(editor, config, ui, stato.token);
  // Classe e variabili del tema vanno sul corpo del canvas (non sul progetto: non diventano contenuto della pagina). Si riapplicano
  // dopo il caricamento perché GrapesJS può riscrivere gli attributi del corpo quando disegna il contenitore principale.
  const applicaTema = () => {
    const corpo = editor.Canvas.getBody();
    if (!corpo) return;
    if (config.tema.classe) corpo.classList.add(config.tema.classe);
    if (config.tema.variabili) corpo.setAttribute('style', config.tema.variabili);
  };
  editor.on('canvas:frame:load:body', applicaTema);
  editor.on('load', () => {
    applicaTema();
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

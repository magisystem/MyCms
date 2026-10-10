// Tipi di componente di GrapesJS generati dal catalogo del server (stessa fonte del registro .NET: nessun elenco duplicato).
// Ogni proprietà e ogni stile del catalogo diventa un "trait" che scrive l'attributo data-p-* / data-s-*; l'adapter lato server
// legge solo questi attributi. L'aspetto nel canvas è calcolato nella vista (classi e testo dell'elemento, mai nel modello),
// quindi non entra nel progetto salvato.
import { consentito } from './vincoli.js';

const FOGLIE = new Set(['contenuto.titolo', 'contenuto.testo', 'contenuto.immagine', 'contenuto.pulsante', 'contenuto.hero', 'contenuto.cta', 'contenuto.voce']);

const TAG = {
  'layout.sezione': 'section',
  'layout.colonne': 'div',
  'layout.colonna': 'div',
  'contenuto.titolo': 'h2',
  'contenuto.testo': 'p',
  'contenuto.immagine': 'img',
  'contenuto.pulsante': 'a',
  'contenuto.hero': 'section',
  'contenuto.cta': 'section',
  'contenuto.fisarmonica': 'div',
  'contenuto.voce': 'div',
};

// Testi modificabili direttamente nel canvas (doppio clic): solo testo semplice, nessun HTML. Gli altri campi restano nel pannello.
const TESTI_IN_LINEA = {
  'contenuto.titolo': { attributo: 'data-p-testo', multilinea: false },
  'contenuto.testo': { attributo: 'data-p-testo', multilinea: true },
  'contenuto.pulsante': { attributo: 'data-p-etichetta', multilinea: false },
};

function valore(componente, attributo, predefinito) {
  const v = componente.getAttributes()[attributo];
  return v === undefined || v === '' ? predefinito : v;
}

/**
 * Modifica del testo nel canvas. Il contenuto resta testo semplice: si legge `textContent` (mai HTML), si applica la lunghezza
 * massima del catalogo e, nei campi a riga singola, si tolgono gli a capo. I nodi bloccati si modificano solo se la proprietà
 * è tra quelle ammesse dal template (le stesse che mostra il pannello). Esc annulla, un clic fuori conferma.
 */
function collegaModificaInLinea(el, modello, impostazione, lunghezzaMassima, aspettoFn) {
  if (el.dataset.dvModifica === '1') return;
  el.dataset.dvModifica = '1';
  el.addEventListener('dblclick', (evento) => {
    if (!modello.get('traits').models.some((t) => t.get('name') === impostazione.attributo)) return;
    evento.preventDefault();
    evento.stopPropagation();
    const iniziale = valore(modello, impostazione.attributo, '');
    el.contentEditable = 'plaintext-only';
    if (el.contentEditable !== 'plaintext-only') el.contentEditable = 'true';
    el.focus();
    const concludi = (conferma) => {
      el.removeEventListener('blur', alBlur);
      el.removeEventListener('keydown', alTasto);
      el.removeEventListener('paste', alIncolla);
      let testo = el.textContent ?? '';
      el.contentEditable = 'false';
      el.removeAttribute('contenteditable');
      testo = testo.replace(/\r\n/g, '\n');
      if (!impostazione.multilinea) testo = testo.replace(/\s*\n\s*/g, ' ');
      testo = testo.trim().slice(0, lunghezzaMassima);
      if (conferma && testo !== iniziale) {
        modello.addAttributes({ [impostazione.attributo]: testo });
      } else {
        aspettoFn();
      }
    };
    const alBlur = () => concludi(true);
    const alTasto = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        el.textContent = iniziale;
        el.blur();
      } else if (e.key === 'Enter' && !impostazione.multilinea) {
        e.preventDefault();
        el.blur();
      }
      e.stopPropagation();
    };
    // Incolla solo testo semplice, anche dove il browser non conosce "plaintext-only".
    const alIncolla = (e) => {
      e.preventDefault();
      const testo = e.clipboardData?.getData('text/plain') ?? '';
      el.ownerDocument.execCommand('insertText', false, testo);
    };
    el.addEventListener('blur', alBlur);
    el.addEventListener('keydown', alTasto);
    el.addEventListener('paste', alIncolla);
  });
}

function impostaClassi(el, classi) {
  for (const classe of [...el.classList]) {
    if (classe.startsWith('dv-')) el.classList.remove(classe);
  }
  el.classList.add(...classi);
}

// Aspetto nel canvas: riproduce le classi del renderer pubblico (stesso foglio di stile caricato nel canvas).
function aspetto(tipo, el, modello) {
  const p = (nome, predefinito = '') => valore(modello, nome, predefinito);
  switch (tipo) {
    case 'layout.sezione':
      impostaClassi(el, ['dv-sezione', `dv-spaziatura-${p('data-s-spaziatura', 'normale')}`, `dv-sfondo-${p('data-s-sfondo', 'nessuno')}`]);
      break;
    case 'layout.colonne':
      impostaClassi(el, ['dv-colonne', `dv-colonne-${Math.min(3, Math.max(1, modello.components().length))}`]);
      break;
    case 'layout.colonna':
      impostaClassi(el, ['dv-colonna']);
      break;
    case 'contenuto.titolo':
      impostaClassi(el, ['dv-titolo', `dv-allineamento-${p('data-s-allineamento', 'sinistra')}`]);
      el.textContent = p('data-p-testo');
      break;
    case 'contenuto.testo':
      impostaClassi(el, ['dv-testo', `dv-allineamento-${p('data-s-allineamento', 'sinistra')}`]);
      el.textContent = p('data-p-testo');
      break;
    case 'contenuto.immagine': {
      impostaClassi(el, ['dv-immagine']);
      const id = p('data-p-id-risorsa');
      el.setAttribute('src', /^[0-9a-f-]{36}$/.test(id) ? `/media/${id}` : 'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22240%22 height=%22120%22%3E%3Crect width=%22100%25%22 height=%22100%25%22 fill=%22%23ccd%22/%3E%3C/svg%3E');
      el.setAttribute('alt', p('data-p-alt'));
      break;
    }
    case 'contenuto.pulsante':
      impostaClassi(el, ['pulsante', 'dv-pulsante', `dv-pulsante-${p('data-s-variante', 'primario')}`]);
      el.textContent = p('data-p-etichetta');
      break;
    case 'contenuto.hero':
      impostaClassi(el, ['dv-hero', `dv-altezza-${p('data-s-altezza', 'normale')}`, `dv-sfondo-${p('data-s-sfondo', 'scuro')}`, `dv-allineamento-${p('data-s-allineamento', 'sinistra')}`]);
      riempi(el, [['h1', 'dv-hero-titolo', p('data-p-titolo')], ['p', 'dv-hero-sottotitolo', p('data-p-sottotitolo')], ['span', 'pulsante dv-pulsante dv-pulsante-primario', p('data-p-etichetta-pulsante')]]);
      break;
    case 'contenuto.cta':
      impostaClassi(el, ['dv-cta', `dv-sfondo-${p('data-s-sfondo', 'chiaro')}`, `dv-allineamento-${p('data-s-allineamento', 'sinistra')}`]);
      riempi(el, [['h2', 'dv-cta-titolo', p('data-p-titolo')], ['p', '', p('data-p-testo')], ['span', 'pulsante dv-pulsante dv-pulsante-primario', p('data-p-etichetta')]]);
      break;
    case 'contenuto.fisarmonica':
      impostaClassi(el, ['dv-fisarmonica']);
      break;
    case 'contenuto.voce':
      impostaClassi(el, ['dv-voce']);
      riempi(el, [['strong', 'dv-voce-domanda', p('data-p-domanda')], ['div', 'dv-voce-risposta', p('data-p-risposta')]]);
      break;
    default:
      break;
  }
}

function riempi(el, voci) {
  el.replaceChildren(
    ...voci
      .filter(([, , testo]) => testo)
      .map(([tag, classe, testo]) => {
        const figlio = document.createElement(tag);
        figlio.className = classe;
        figlio.textContent = testo;
        return figlio;
      }),
  );
}

function creaTrait(definizione, config) {
  const etichetta = config.testi.campi[definizione.nome] ?? definizione.nome;
  const base = { name: definizione.attributo, label: etichetta, default: definizione.predefinito };
  switch (definizione.tipo) {
    case 'Enumerazione':
      return { ...base, type: 'select', options: definizione.valori.map((v) => ({ id: v, label: v })) };
    case 'IdRisorsa':
      return {
        ...base,
        type: 'select',
        options: [{ id: '', label: config.testi.nessunaRisorsa }, ...config.risorse.map((r) => ({ id: r.id, label: r.nome }))],
      };
    case 'Booleano':
      return { ...base, type: 'checkbox', valueTrue: 'si', valueFalse: 'no' };
    case 'TestoLungo':
      return { ...base, type: 'dv-textarea', maxlength: definizione.lunghezzaMassima };
    default:
      return { ...base, type: 'text', placeholder: definizione.tipo === 'Url' ? '/pagina oppure https://…' : undefined, maxlength: definizione.lunghezzaMassima };
  }
}

export function registraComponenti(editor, catalogo, vincoli, config) {
  // Campo di testo su più righe: GrapesJS non lo prevede tra i trait di base.
  editor.TraitManager.addType('dv-textarea', {
    createInput({ trait }) {
      const area = document.createElement('textarea');
      area.rows = 6;
      area.maxLength = trait.get('maxlength') || 5000;
      return area;
    },
    onUpdate({ elInput, component, trait }) {
      elInput.value = component.getAttributes()[trait.getName()] ?? '';
    },
    onEvent({ elInput, component, trait }) {
      component.addAttributes({ [trait.getName()]: elInput.value });
    },
  });

  const definizioni = new Map(catalogo.map((c) => [c.tipo, c]));
  for (const definizione of catalogo) {
    const tipo = definizione.tipo;
    const tratti = [...definizione.proprieta, ...definizione.stili].map((d) => creaTrait(d, config));
    editor.DomComponents.addType(tipo, {
      isComponent: () => false,
      model: {
        defaults: {
          tagName: TAG[tipo] ?? 'div',
          name: config.testi.componenti[tipo] ?? tipo,
          traits: tratti,
          droppable: FOGLIE.has(tipo) ? false : (sorgente, destinazione) => consentito(destinazione, sorgente?.get?.('type'), vincoli, definizioni),
          copyable: true,
          removable: true,
          draggable: true,
          stylable: false,
        },
        init() {
          if (tipo === 'contenuto.titolo') {
            const aggiorna = () => this.set('tagName', `h${valore(this, 'data-p-livello', '2')}`);
            this.on('change:attributes', aggiorna);
            aggiorna();
          }
        },
      },
      view: {
        init() {
          this.listenTo(this.model, 'change:attributes', () => aspetto(tipo, this.el, this.model));
          if (tipo === 'layout.colonne') {
            this.listenTo(this.model.components(), 'add remove reset', () => aspetto(tipo, this.el, this.model));
          }
        },
        onRender() {
          aspetto(tipo, this.el, this.model);
          const inLinea = TESTI_IN_LINEA[tipo];
          if (inLinea) {
            const massima = [...definizione.proprieta, ...definizione.stili].find((d) => d.attributo === inLinea.attributo)?.lunghezzaMassima || 5000;
            collegaModificaInLinea(this.el, this.model, inLinea, massima, () => aspetto(tipo, this.el, this.model));
          }
        },
      },
    });
  }
}

const CATEGORIE = { layout: 'Layout', contenuto: 'Contenuto' };

function contenutoBlocco(tipo, config) {
  switch (tipo) {
    case 'layout.colonne':
      return { type: tipo, components: [{ type: 'layout.colonna' }, { type: 'layout.colonna' }] };
    case 'contenuto.titolo':
      return { type: tipo, attributes: { 'data-p-testo': config.testi.componenti[tipo] ?? 'Titolo' } };
    case 'contenuto.testo':
      return { type: tipo, attributes: { 'data-p-testo': config.testi.componenti[tipo] ?? 'Testo' } };
    case 'contenuto.immagine':
      return { type: tipo, attributes: { 'data-p-id-risorsa': config.risorse[0]?.id ?? '', 'data-p-alt': config.risorse[0]?.alt ?? '' } };
    case 'contenuto.pulsante':
      return { type: tipo, attributes: { 'data-p-etichetta': config.testi.componenti[tipo] ?? 'Pulsante', 'data-p-url': '/' } };
    case 'contenuto.hero':
      return { type: tipo, attributes: { 'data-p-titolo': config.testi.componenti[tipo] ?? 'Titolo' } };
    case 'contenuto.cta':
      return { type: tipo, attributes: { 'data-p-titolo': config.testi.componenti[tipo] ?? 'Titolo', 'data-p-etichetta': 'Scopri', 'data-p-url': '/' } };
    case 'contenuto.fisarmonica':
      return { type: tipo, components: [{ type: 'contenuto.voce', attributes: { 'data-p-domanda': 'Domanda', 'data-p-risposta': 'Risposta' } }] };
    default:
      return { type: tipo };
  }
}

export function creaBlocchi(editor, definizioni, config) {
  for (const [tipo] of definizioni) {
    // Una colonna o una voce da sole non hanno senso: nascono dentro "Colonne" e "Fisarmonica"; un'immagine richiede almeno una risorsa nella libreria.
    if (tipo === 'layout.colonna' || tipo === 'contenuto.voce') continue;
    if (tipo === 'contenuto.immagine' && config.risorse.length === 0) continue;
    editor.BlockManager.add(`dv-${tipo}`, {
      label: config.testi.componenti[tipo] ?? tipo,
      category: CATEGORIE[tipo.split('.')[0]] ?? '',
      content: contenutoBlocco(tipo, config),
      select: true,
    });
  }
}

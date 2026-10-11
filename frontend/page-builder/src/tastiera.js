// Uso dell'editor da tastiera e con lo screen reader. Il canvas di GrapesJS è un iframe pensato per il mouse: qui ci sono le
// alternative equivalenti, fuori dal canvas, tutte basate su controlli nativi o su schemi WAI-ARIA noti.
//  - Blocchi: ogni blocco è un pulsante; Invio o Spazio lo inserisce (dentro il componente selezionato, altrimenti dopo di esso,
//    altrimenti nella pagina), rispettando gli stessi vincoli del trascinamento (`droppable`).
//  - Struttura: albero (role=tree) dei componenti con frecce, Home, Fine; la selezione segue il focus.
//  - Azioni sul componente selezionato: sposta su/giù, duplica, elimina (disabilitate dove il template lo vieta).
// Come per il resto dell'editor, nulla qui è un'autorità: il server riconfronta tutto a ogni salvataggio.
import { consentito } from './vincoli.js';

const LUCCHETTO = ' 🔒';

function elemento(tag, classe, testo) {
  const el = document.createElement(tag);
  if (classe) el.className = classe;
  if (testo !== undefined) el.textContent = testo;
  return el;
}

function formatta(modello, ...valori) {
  return valori.reduce((testo, valore, indice) => testo.split(`{${indice}}`).join(valore), modello ?? '');
}

/** Nome leggibile del componente, senza il simbolo del lucchetto (che per lo screen reader diventa una parola a parte). */
export function nomeComponente(componente) {
  return String(componente.getName?.() ?? componente.get('type')).replace(LUCCHETTO, '');
}

/** Rende i blocchi pulsanti raggiungibili con Tab e attivabili con Invio/Spazio (l'inserimento è `appendOnClick` di GrapesJS). */
export function rendiBlocchiAccessibili(contenitore, testi) {
  const applica = () => {
    for (const blocco of contenitore.querySelectorAll('.gjs-block')) {
      if (blocco.dataset.dvTastiera === '1') continue;
      blocco.dataset.dvTastiera = '1';
      const etichetta = blocco.querySelector('.gjs-block-label')?.textContent?.trim() ?? blocco.getAttribute('title') ?? '';
      blocco.tabIndex = 0;
      blocco.setAttribute('role', 'button');
      blocco.setAttribute('aria-label', formatta(testi.inserisciBlocco, etichetta));
      blocco.addEventListener('keydown', (evento) => {
        if (evento.key === 'Enter' || evento.key === ' ') {
          evento.preventDefault();
          blocco.click();
        }
      });
    }
  };
  applica();
  new MutationObserver(applica).observe(contenitore, { childList: true, subtree: true });
}

/**
 * GrapesJS disegna le proprietà con un'etichetta (<div class="gjs-label">) che non è collegata al campo: senza questo un lettore di
 * schermo legge "campo di testo" senza dire quale. Il nome del campo diventa l'etichetta della proprietà.
 */
export function nomiCampiProprieta(contenitore) {
  const applica = () => {
    for (const riga of contenitore.querySelectorAll('.gjs-trt-trait')) {
      const nome = riga.querySelector('.gjs-label')?.textContent?.trim();
      if (!nome) continue;
      for (const campo of riga.querySelectorAll('input, select, textarea')) {
        if (campo.getAttribute('aria-label') !== nome) campo.setAttribute('aria-label', nome);
      }
    }
  };
  applica();
  new MutationObserver(applica).observe(contenitore, { childList: true, subtree: true });
}

/**
 * Albero dei componenti. Un solo elemento ha tabindex=0 (il selezionato, altrimenti il primo): Tab entra ed esce dall'albero,
 * le frecce si muovono dentro (roving tabindex).
 */
export function creaStruttura(editor, contenitore, { testi, definizioni, vincoli, annuncia, vaiAProprieta }) {
  const albero = elemento('ul', 'dv-albero');
  albero.setAttribute('role', 'tree');
  albero.setAttribute('aria-label', testi.struttura);
  contenitore.append(albero);

  const compressi = new Set();
  let voci = []; // elementi visibili, in ordine di lettura
  let programmato = false;

  const vuoto = elemento('p', 'dv-albero-vuoto', testi.strutturaVuota);
  contenitore.append(vuoto);

  const figli = (componente) => componente.components().models.filter((c) => definizioni.has(c.get('type')));
  const bloccato = (componente) => Boolean(vincoli.nodi[componente.getAttributes().id]);

  function disegna(componente, livello) {
    const li = elemento('li', 'dv-albero-voce');
    li.setAttribute('role', 'none');
    const discendenti = figli(componente);
    const riga = elemento('div', 'dv-albero-riga');
    riga.setAttribute('role', 'treeitem');
    riga.setAttribute('aria-level', String(livello));
    riga.setAttribute('aria-selected', 'false');
    riga.tabIndex = -1;
    riga.dataset.cid = componente.cid;
    riga.style.paddingInlineStart = `${0.5 + (livello - 1) * 1}rem`;
    const nome = nomeComponente(componente);
    const fisso = bloccato(componente);
    riga.setAttribute('aria-label', fisso ? `${nome} (${testi.bloccatoBreve})` : nome);
    riga.append(elemento('span', 'dv-albero-nome', nome));
    if (fisso) {
      const lucchetto = elemento('span', 'dv-albero-lucchetto', LUCCHETTO.trim());
      lucchetto.setAttribute('aria-hidden', 'true');
      riga.append(lucchetto);
    }
    li.append(riga);
    voci.push(riga);
    if (discendenti.length > 0) {
      const aperto = !compressi.has(componente.cid);
      riga.setAttribute('aria-expanded', aperto ? 'true' : 'false');
      if (aperto) {
        const gruppo = elemento('ul', 'dv-albero-gruppo');
        gruppo.setAttribute('role', 'group');
        for (const figlio of discendenti) gruppo.append(disegna(figlio, livello + 1));
        li.append(gruppo);
      }
    }
    return li;
  }

  function trova(cid, radice = editor.getWrapper()) {
    if (radice.cid === cid) return radice;
    for (const figlio of radice.components().models) {
      const trovato = trova(cid, figlio);
      if (trovato) return trovato;
    }
    return null;
  }

  function ridisegna() {
    programmato = false;
    const avevaFocus = contenitore.contains(document.activeElement);
    voci = [];
    albero.textContent = '';
    const radici = figli(editor.getWrapper());
    for (const componente of radici) albero.append(disegna(componente, 1));
    vuoto.hidden = radici.length > 0;
    albero.hidden = radici.length === 0;
    sincronizzaSelezione(avevaFocus);
  }

  function sincronizzaSelezione(portaFocus) {
    const selezionato = editor.getSelected();
    let attivo = null;
    for (const riga of voci) {
      const sel = Boolean(selezionato) && riga.dataset.cid === selezionato.cid;
      riga.setAttribute('aria-selected', sel ? 'true' : 'false');
      if (sel) attivo = riga;
    }
    // Il componente selezionato può stare dentro un ramo compresso: la riga non c'è, il primo resta raggiungibile.
    const tabulabile = attivo ?? voci[0] ?? null;
    for (const riga of voci) riga.tabIndex = riga === tabulabile ? 0 : -1;
    if (portaFocus && tabulabile) tabulabile.focus({ preventScroll: false });
  }

  function programma() {
    if (programmato) return;
    programmato = true;
    queueMicrotask(ridisegna);
  }

  function vaiA(riga) {
    if (!riga) return;
    const componente = trova(riga.dataset.cid);
    if (!componente) return;
    editor.select(componente);
    riga.focus();
  }

  albero.addEventListener('click', (evento) => {
    const riga = evento.target.closest('[role=treeitem]');
    if (riga) vaiA(riga);
  });

  albero.addEventListener('keydown', (evento) => {
    const riga = evento.target.closest('[role=treeitem]');
    if (!riga || evento.altKey || evento.ctrlKey || evento.metaKey) return;
    const indice = voci.indexOf(riga);
    const componente = trova(riga.dataset.cid);
    if (!componente) return;
    const espandibile = riga.hasAttribute('aria-expanded');
    const aperta = riga.getAttribute('aria-expanded') === 'true';
    let gestito = true;
    switch (evento.key) {
      case 'ArrowDown': vaiA(voci[indice + 1]); break;
      case 'ArrowUp': vaiA(voci[indice - 1]); break;
      case 'Home': vaiA(voci[0]); break;
      case 'End': vaiA(voci[voci.length - 1]); break;
      case 'ArrowRight':
        if (espandibile && !aperta) {
          compressi.delete(componente.cid);
          ridisegna();
        } else if (espandibile) {
          vaiA(voci[indice + 1]);
        }
        break;
      case 'ArrowLeft':
        if (espandibile && aperta) {
          compressi.add(componente.cid);
          ridisegna();
        } else {
          const genitore = componente.parent();
          const rigaGenitore = genitore && voci.find((r) => r.dataset.cid === genitore.cid);
          vaiA(rigaGenitore);
        }
        break;
      case 'Enter':
      case 'F2':
        vaiAProprieta();
        break;
      case 'Escape':
        editor.select();
        break;
      default:
        gestito = false;
    }
    if (gestito) evento.preventDefault();
  });

  editor.on('component:add component:remove component:update component:selected component:deselected load', programma);
  programma();
  return { ridisegna: programma };
}

/** Pulsanti per agire sul componente selezionato (sostituiscono il trascinamento e la barra del canvas). */
export function creaAzioni(editor, contenitore, { testi, definizioni, vincoli, annuncia }) {
  contenitore.setAttribute('role', 'toolbar');
  contenitore.setAttribute('aria-label', testi.azioni);
  const pulsanti = {};
  const crea = (chiave, testo, azione) => {
    const pulsante = elemento('button', 'dv-azione', testo);
    pulsante.type = 'button';
    pulsante.addEventListener('click', () => {
      const selezionato = editor.getSelected();
      if (selezionato && !pulsante.disabled) azione(selezionato);
      aggiorna();
    });
    pulsanti[chiave] = pulsante;
    contenitore.append(pulsante);
  };

  const sposta = (componente, delta) => {
    const genitore = componente.parent();
    const indice = genitore.components().indexOf(componente);
    // `move` di GrapesJS vuole la posizione di inserimento prima della rimozione: scendere di uno significa inserire dopo il successivo.
    componente.move(genitore, { at: delta > 0 ? indice + 2 : indice - 1 });
    editor.select(componente);
    const nuovo = genitore.components().indexOf(componente);
    annuncia(formatta(testi.spostato, nomeComponente(componente), String(nuovo + 1), String(genitore.components().length)));
  };

  crea('su', testi.spostaSu, (c) => sposta(c, -1));
  crea('giu', testi.spostaGiu, (c) => sposta(c, 1));
  crea('duplica', testi.duplica, (c) => {
    const genitore = c.parent();
    const indice = genitore.components().indexOf(c);
    const copia = genitore.append(c.clone(), { at: indice + 1 })[0];
    if (copia) {
      editor.select(copia);
      annuncia(formatta(testi.duplicato, nomeComponente(c)));
    }
  });
  crea('elimina', testi.elimina, (c) => {
    const nome = nomeComponente(c);
    const genitore = c.parent();
    c.remove();
    const wrapper = editor.getWrapper();
    if (genitore && genitore !== wrapper) editor.select(genitore);
    else editor.select();
    annuncia(formatta(testi.eliminato, nome));
  });

  function aggiorna() {
    const c = editor.getSelected();
    const genitore = c?.parent();
    const mobile = Boolean(c) && Boolean(genitore) && c.get('draggable') !== false && !vincoli.nodi[c.getAttributes().id];
    const indice = mobile ? genitore.components().indexOf(c) : -1;
    const fratelli = mobile ? genitore.components().models : [];
    // Non si scavalca un nodo del template: l'ordine dei nodi bloccati lo decide il template.
    const fisso = (fratello) => !fratello || Boolean(vincoli.nodi[fratello.getAttributes().id]);
    pulsanti.su.disabled = !mobile || indice <= 0 || fisso(fratelli[indice - 1]);
    pulsanti.giu.disabled = !mobile || indice >= fratelli.length - 1 || fisso(fratelli[indice + 1]);
    pulsanti.duplica.disabled = !c || !genitore || c.get('copyable') === false || !consentito(genitore, c.get('type'), vincoli, definizioni);
    pulsanti.elimina.disabled = !c || c.get('removable') === false;
  }

  editor.on('component:selected component:deselected component:add component:remove load', () => queueMicrotask(aggiorna));
  aggiorna();
}

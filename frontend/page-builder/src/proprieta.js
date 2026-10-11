// Pannello delle proprietà (M5): oltre ai campi di GrapesJS aggiunge, senza toccare il modello del documento, i raggruppamenti
// (Contenuto, Aspetto, Animazione), l'indicazione dei campi obbligatori, il contatore dei caratteri e il controllo immediato dei campi
// obbligatori. È solo guida per chi scrive: il server riconfronta tutto al salvataggio (i campi mancanti tornano come errori per nodo).

function elemento(tag, classe, testo) {
  const el = document.createElement(tag);
  if (classe) el.className = classe;
  if (testo !== undefined) el.textContent = testo;
  return el;
}

/** Gruppo di una proprietà: le proprietà sono il contenuto, gli stili l'aspetto, tranne l'animazione. */
export function gruppoDi(definizione, eStile) {
  if (!eStile) return 'contenuto';
  return definizione.nome === 'animazione' || definizione.nome === 'ritardoAnimazione' ? 'animazione' : 'aspetto';
}

export function migliora(contenitore, editor, testi) {
  let applicando = false;
  const osservatore = new MutationObserver(() => {
    if (!applicando) applica();
  });

  function applica() {
    const selezionato = editor.getSelected();
    const righe = [...contenitore.querySelectorAll('.gjs-trt-trait')];
    const tratti = selezionato?.get('traits')?.models ?? [];
    // Le righe seguono l'ordine dei tratti del componente selezionato: se non coincidono (disegno a metà) si aspetta il prossimo giro.
    if (righe.length === 0 || righe.length !== tratti.length) return;
    applicando = true;
    try {
      let precedente = null;
      righe.forEach((riga, indice) => {
        const tratto = tratti[indice];
        const gruppo = tratto.get('gruppo');
        if (gruppo && gruppo !== precedente && !riga.previousElementSibling?.classList.contains('dv-gruppo')) {
          riga.before(elemento('h3', 'dv-gruppo', testi.gruppi?.[gruppo] ?? gruppo));
        }
        precedente = gruppo ?? precedente;
        if (riga.dataset.dvMigliorata === tratto.cid) return;
        riga.dataset.dvMigliorata = tratto.cid;
        migliorRiga(riga, tratto);
      });
    } finally {
      applicando = false;
    }
  }

  function migliorRiga(riga, tratto) {
    const campo = riga.querySelector('textarea, input:not([type=checkbox]), select');
    if (!campo) return;
    const obbligatoria = Boolean(tratto.get('obbligatoria'));
    if (obbligatoria) {
      campo.setAttribute('aria-required', 'true');
      const etichetta = riga.querySelector('.gjs-label');
      if (etichetta && !etichetta.querySelector('.dv-obbligatorio')) {
        const segno = elemento('span', 'dv-obbligatorio', ' *');
        segno.setAttribute('aria-hidden', 'true');
        etichetta.append(segno);
      }
    }
    const massimo = Number(tratto.get('maxlength')) || 0;
    const nota = elemento('div', 'dv-campo-nota');
    nota.id = `dv-nota-${tratto.cid}`;
    const aggiorna = () => {
      const vuoto = campo.value.trim() === '';
      const errore = obbligatoria && vuoto;
      campo.setAttribute('aria-invalid', errore ? 'true' : 'false');
      campo.classList.toggle('dv-campo-non-valido', errore);
      const parti = [];
      if (errore) parti.push(testi.campoObbligatorio ?? 'Campo obbligatorio');
      if (massimo > 0 && campo.tagName !== 'SELECT') parti.push(`${campo.value.length} / ${massimo}`);
      nota.textContent = parti.join(' · ');
      nota.classList.toggle('dv-campo-errore', errore);
    };
    campo.setAttribute('aria-describedby', nota.id);
    campo.addEventListener('input', aggiorna);
    campo.addEventListener('change', aggiorna);
    campo.addEventListener('blur', aggiorna);
    (campo.closest('.gjs-field') ?? campo.parentElement).after(nota);
    aggiorna();
  }

  osservatore.observe(contenitore, { childList: true, subtree: true });
  editor.on('component:selected component:deselected', () => queueMicrotask(applica));
}

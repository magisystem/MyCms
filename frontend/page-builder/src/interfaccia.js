// Scheletro dell'interfaccia: barra superiore, colonne dei blocchi / canvas / proprietà e livelli, riquadro degli errori.
// Si costruisce con l'API del DOM e textContent: nessun testo del server o dell'utente entra mai come HTML.

function elemento(tag, classe, testo) {
  const el = document.createElement(tag);
  if (classe) el.className = classe;
  if (testo !== undefined) el.textContent = testo;
  return el;
}

export function creaInterfaccia(radice, config) {
  const testi = config.testi;
  radice.textContent = '';

  const barra = elemento('header', 'dv-barra');
  const indietro = elemento('a', 'dv-collegamento', testi.torna);
  indietro.href = config.urlPagina;
  const titolo = elemento('span', 'dv-titolo-pagina');
  const dispositivi = elemento('div', 'dv-dispositivi');
  dispositivi.setAttribute('role', 'group');
  dispositivi.setAttribute('aria-label', testi.dispositivi ?? 'Dispositivi');
  const stato = elemento('span', 'dv-stato');
  stato.setAttribute('role', 'status');
  const anteprima = elemento('a', 'dv-collegamento', testi.anteprima);
  anteprima.href = config.urlAnteprima;
  anteprima.target = '_blank';
  anteprima.rel = 'noopener';
  const salva = elemento('button', 'dv-pulsante-salva', testi.salva);
  salva.type = 'button';
  const storico = elemento('div', 'dv-storico');
  storico.setAttribute('role', 'group');
  storico.setAttribute('aria-label', testi.cronologia ?? '');
  const annulla = elemento('button', 'dv-dispositivo', testi.annulla);
  const ripeti = elemento('button', 'dv-dispositivo', testi.ripeti);
  annulla.type = 'button';
  ripeti.type = 'button';
  storico.append(annulla, ripeti);
  barra.append(indietro, titolo, dispositivi, storico, stato, anteprima, salva);

  // Collegamenti rapidi alle tre aree (visibili solo quando hanno il focus) e area per gli annunci dello screen reader.
  const rapidi = elemento('nav', 'dv-rapidi');
  rapidi.setAttribute('aria-label', testi.vaiA ?? '');
  const annunci = elemento('div', 'dv-solo-lettori');
  annunci.setAttribute('role', 'status');
  annunci.setAttribute('aria-live', 'polite');

  const errori = elemento('div', 'dv-errori');
  errori.setAttribute('role', 'alert');
  errori.hidden = true;

  const corpo = elemento('div', 'dv-corpo');
  const sinistra = elemento('aside', 'dv-colonna dv-sinistra');
  const titoloBlocchi = elemento('h2', 'dv-intestazione', testi.blocchi);
  titoloBlocchi.id = 'dv-titolo-blocchi';
  sinistra.setAttribute('aria-labelledby', titoloBlocchi.id);
  const blocchi = elemento('div', 'dv-blocchi');
  sinistra.append(titoloBlocchi, elemento('p', 'dv-aiuto', testi.aiutoBlocchi), blocchi);
  const centro = elemento('div', 'dv-canvas');
  centro.setAttribute('role', 'region');
  centro.setAttribute('aria-label', testi.anteprimaPagina ?? '');
  const destra = elemento('aside', 'dv-colonna dv-destra');
  const titoloStruttura = elemento('h2', 'dv-intestazione', testi.struttura);
  titoloStruttura.id = 'dv-titolo-struttura';
  destra.setAttribute('aria-label', `${testi.struttura} / ${testi.proprieta}`);
  const azioni = elemento('div', 'dv-azioni');
  const struttura = elemento('div', 'dv-struttura');
  const titoloProprieta = elemento('h2', 'dv-intestazione', testi.proprieta);
  titoloProprieta.id = 'dv-titolo-proprieta';
  const proprieta = elemento('div', 'dv-proprieta');
  proprieta.setAttribute('role', 'group');
  proprieta.setAttribute('aria-labelledby', titoloProprieta.id);
  proprieta.tabIndex = -1;
  destra.append(titoloStruttura, azioni, struttura, titoloProprieta, proprieta);
  corpo.append(sinistra, centro, destra);

  const aree = [
    [testi.vaiBlocchi, () => blocchi.querySelector('[role=button]')],
    [testi.vaiStruttura, () => struttura.querySelector('[role=treeitem][tabindex="0"]') ?? struttura.querySelector('[role=treeitem]') ?? azioni.querySelector('button:not(:disabled)')],
    [testi.vaiProprieta, () => proprieta.querySelector('input, select, textarea, button') ?? proprieta],
  ];
  for (const [testo, bersaglio] of aree) {
    const pulsante = elemento('button', 'dv-rapido', testo);
    pulsante.type = 'button';
    pulsante.addEventListener('click', () => bersaglio()?.focus());
    rapidi.append(pulsante);
  }

  radice.append(rapidi, barra, annunci, errori, corpo);

  return {
    contenitoreCanvas: centro,
    contenitoreBlocchi: blocchi,
    contenitoreProprieta: proprieta,
    contenitoreStruttura: struttura,
    contenitoreAzioni: azioni,
    /** Primo campo del pannello delle proprietà (o il pannello stesso, che è focalizzabile). */
    vaiAProprieta() {
      (proprieta.querySelector('input, select, textarea, button') ?? proprieta).focus();
    },
    /** Annuncio per lo screen reader (area aria-live): ogni azione da tastiera dice cosa ha fatto. */
    annuncia(testo) {
      annunci.textContent = '';
      // Un nodo nuovo a ogni annuncio: lo stesso testo ripetuto viene letto di nuovo.
      requestAnimationFrame(() => { annunci.textContent = testo; });
    },
    pulsanteSalva: salva,
    pulsanteAnnulla: annulla,
    pulsanteRipeti: ripeti,
    /** Pulsanti Desktop / Tablet / Mobile: cambiano solo la larghezza del canvas (le regole CSS responsive sono quelle del sito). */
    creaDispositivi(etichette, alCambio) {
      for (const [id, etichetta] of [['desktop', etichette.desktop], ['tablet', etichette.tablet], ['mobile', etichette.mobile]]) {
        const pulsante = elemento('button', 'dv-dispositivo', etichetta ?? id);
        pulsante.type = 'button';
        pulsante.dataset.dispositivo = id;
        pulsante.setAttribute('aria-pressed', id === 'desktop' ? 'true' : 'false');
        pulsante.addEventListener('click', () => {
          alCambio(id);
          for (const altro of dispositivi.children) {
            altro.setAttribute('aria-pressed', altro === pulsante ? 'true' : 'false');
          }
        });
        dispositivi.append(pulsante);
      }
    },
    impostaTitolo(testo) {
      titolo.textContent = testo ?? '';
    },
    mostraStato(testo, tipo) {
      stato.textContent = testo ?? '';
      stato.dataset.tipo = tipo ?? '';
    },
    mostraErrori(intestazione, voci, alSelezione) {
      errori.textContent = '';
      if (!voci || voci.length === 0) {
        errori.hidden = true;
        return;
      }
      errori.append(elemento('strong', null, intestazione));
      const elenco = elemento('ul');
      for (const voce of voci) {
        const riga = elemento('li');
        const testo = voce.testo;
        if (voce.idNodo && alSelezione) {
          const pulsante = elemento('button', 'dv-errore-nodo', testo);
          pulsante.type = 'button';
          pulsante.addEventListener('click', () => alSelezione(voce.idNodo));
          riga.append(pulsante);
        } else {
          riga.textContent = testo;
        }
        elenco.append(riga);
      }
      errori.append(elenco);
      errori.hidden = false;
    },
    nascondiErrori() {
      errori.hidden = true;
      errori.textContent = '';
    },
  };
}

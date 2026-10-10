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
  barra.append(indietro, titolo, dispositivi, stato, anteprima, salva);

  const errori = elemento('div', 'dv-errori');
  errori.setAttribute('role', 'alert');
  errori.hidden = true;

  const corpo = elemento('div', 'dv-corpo');
  const sinistra = elemento('aside', 'dv-colonna dv-sinistra');
  const blocchi = elemento('div', 'dv-blocchi');
  sinistra.append(elemento('h2', 'dv-intestazione', testi.blocchi), blocchi);
  const centro = elemento('div', 'dv-canvas');
  const destra = elemento('aside', 'dv-colonna dv-destra');
  const proprieta = elemento('div', 'dv-proprieta');
  const livelli = elemento('div', 'dv-livelli');
  destra.append(elemento('h2', 'dv-intestazione', testi.proprieta), proprieta, elemento('h2', 'dv-intestazione', testi.livelli), livelli);
  corpo.append(sinistra, centro, destra);

  radice.append(barra, errori, corpo);

  return {
    contenitoreCanvas: centro,
    contenitoreBlocchi: blocchi,
    contenitoreProprieta: proprieta,
    contenitoreLivelli: livelli,
    pulsanteSalva: salva,
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

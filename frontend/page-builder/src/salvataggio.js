// Salvataggio della bozza: debounce non necessario (PoC con salvataggio esplicito), token di concorrenza a ogni invio.
// Il server restituisce il nuovo token; in caso di conflitto non si sovrascrive nulla.

export class Salvataggio {
  constructor(editor, config, ui, token) {
    this.editor = editor;
    this.config = config;
    this.ui = ui;
    this.token = token;
    this.modificato = false;
    this.inCorso = false;
  }

  segnaModificato() {
    if (!this.modificato) {
      this.modificato = true;
      this.ui.mostraStato(this.config.testi.nonSalvato, 'sporco');
    }
  }

  segnaPulito() {
    this.modificato = false;
    this.ui.mostraStato('', '');
  }

  async salva() {
    if (this.inCorso) return;
    this.inCorso = true;
    this.ui.pulsanteSalva.disabled = true;
    this.ui.mostraStato(this.config.testi.salvataggio, 'attesa');
    this.ui.nascondiErrori();
    try {
      const risposta = await fetch(this.config.urlSalva, {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
          RequestVerificationToken: this.config.tokenAntiforgery,
        },
        body: JSON.stringify({ token: this.token, progetto: JSON.stringify(this.editor.getProjectData()) }),
      });
      let corpo = null;
      try {
        corpo = await risposta.json();
      } catch {
        corpo = null;
      }
      if (risposta.ok && corpo?.ok) {
        this.token = corpo.token;
        this.segnaPulito();
        this.ui.mostraStato(corpo.nuovaRevisione ? this.config.testi.salvato : this.config.testi.nessunaModifica, 'ok');
      } else if (risposta.status === 409) {
        this.ui.mostraStato(this.config.testi.conflitto, 'errore');
      } else if (corpo?.errori?.length) {
        this.ui.mostraStato(this.config.testi.erroreGenerico, 'errore');
        this.ui.mostraErrori(
          this.config.testi.erroriValidazione,
          corpo.errori.map((e) => ({ idNodo: e.idNodo, testo: descriviErrore(e, this.config.testi) })),
          (idNodo) => this.selezionaNodo(idNodo),
        );
      } else {
        this.ui.mostraStato(this.config.testi.erroreGenerico, 'errore');
      }
    } catch {
      this.ui.mostraStato(this.config.testi.erroreRete, 'errore');
    } finally {
      this.inCorso = false;
      this.ui.pulsanteSalva.disabled = false;
    }
  }

  selezionaNodo(idNodo) {
    const trovato = this.editor.getWrapper()?.find(`[id="${CSS.escape(idNodo)}"]`)[0];
    if (trovato) {
      this.editor.select(trovato);
    }
  }
}

// Il codice di errore è stabile e arriva dal server; il testo mostrato non contiene mai valori inviati dall'utente.
function descriviErrore(errore, testi) {
  const nodo = errore.idNodo ? ` [${errore.idNodo}]` : '';
  const campo = errore.campo ? ` (${errore.campo})` : '';
  return `${errore.codice}${nodo}${campo}`;
}

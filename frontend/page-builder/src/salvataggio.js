// Salvataggio della bozza con token di concorrenza a ogni invio. Il server restituisce il nuovo token; in caso di conflitto non si
// sovrascrive nulla. Salvataggio automatico controllato (E-005): parte dopo qualche secondo senza modifiche, mai più spesso di un
// intervallo minimo, si sospende da solo se la pagina ha errori e non mostra il riquadro degli errori (lo fa solo il salvataggio
// manuale). Il server tiene in cima alla cronologia una sola revisione automatica per autore.

const ATTESA_AUTOMATICO_MS = 5000;
const INTERVALLO_MINIMO_MS = 15000;

export class Salvataggio {
  constructor(editor, config, ui, token) {
    this.editor = editor;
    this.config = config;
    this.ui = ui;
    this.token = token;
    this.modificato = false;
    this.inCorso = false;
    this.timer = null;
    this.ultimoSalvataggio = 0;
    this.bloccato = false;
    this.modificheInCorso = false;
  }

  segnaModificato() {
    if (this.inCorso) {
      this.modificheInCorso = true;
    }
    if (!this.modificato) {
      this.modificato = true;
      this.ui.mostraStato(this.config.testi.nonSalvato, 'sporco');
    }
    this.pianificaAutomatico();
  }

  pianificaAutomatico() {
    if (this.bloccato) return;
    clearTimeout(this.timer);
    const attesa = Math.max(ATTESA_AUTOMATICO_MS, this.ultimoSalvataggio + INTERVALLO_MINIMO_MS - Date.now());
    this.timer = setTimeout(() => {
      if (this.modificato && !this.inCorso) {
        this.salva(true);
      }
    }, attesa);
  }

  segnaPulito() {
    clearTimeout(this.timer);
    this.modificato = false;
    this.ui.mostraStato('', '');
  }

  async salva(automatico = false) {
    if (this.inCorso) return;
    this.inCorso = true;
    clearTimeout(this.timer);
    this.ui.pulsanteSalva.disabled = true;
    this.ui.mostraStato(this.config.testi.salvataggio, 'attesa');
    if (!automatico) this.ui.nascondiErrori();
    try {
      const risposta = await fetch(this.config.urlSalva, {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
          RequestVerificationToken: this.config.tokenAntiforgery,
        },
        body: JSON.stringify({ token: this.token, progetto: JSON.stringify(this.editor.getProjectData()), automatico }),
      });
      let corpo = null;
      try {
        corpo = await risposta.json();
      } catch {
        corpo = null;
      }
      this.ultimoSalvataggio = Date.now();
      if (risposta.ok && corpo?.ok) {
        this.token = corpo.token;
        this.segnaPulito();
        const ora = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        this.ui.mostraStato(
          automatico ? `${this.config.testi.salvatoAutomatico} ${ora}` : corpo.nuovaRevisione ? this.config.testi.salvato : this.config.testi.nessunaModifica,
          'ok',
        );
      } else if (risposta.status === 409) {
        // Conflitto: nessun altro tentativo automatico, per non insistere su una pagina cambiata altrove.
        this.bloccato = true;
        this.ui.mostraStato(this.config.testi.conflitto, 'errore');
      } else if (automatico) {
        this.ui.mostraStato(this.config.testi.automaticoSospeso, 'sporco');
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
      // Modifiche arrivate mentre si salvava: la pagina torna "non salvata" e si riprova con la stessa cautela. Dopo un errore
      // di validazione non si insiste: il salvataggio automatico riparte alla modifica successiva.
      if (this.modificheInCorso) {
        this.modificheInCorso = false;
        this.modificato = true;
        this.pianificaAutomatico();
      }
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

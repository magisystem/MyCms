// Vincoli dei template applicati all'interfaccia: nodi bloccati (non spostabili né eliminabili, solo le proprietà ammesse
// modificabili) e slot (solo i tipi elencati). Non sono una difesa: il server riconfronta il documento col template a ogni salvataggio.

/** Può un componente di tipo `tipoFiglio` essere inserito dentro `destinazione`? */
export function consentito(destinazione, tipoFiglio, vincoli, definizioni) {
  if (!destinazione || !tipoFiglio) return false;
  const tipoDestinazione = destinazione.get('type');
  if (tipoDestinazione === 'wrapper') {
    return vincoli.radice ? vincoli.radice.tipiAmmessi.includes(tipoFiglio) && destinazione.components().length < vincoli.radice.figliMassimi : false;
  }
  const definizione = definizioni.get(tipoDestinazione);
  if (!definizione || !definizione.figliAmmessi.includes(tipoFiglio)) return false;
  const vincolo = vincoli.nodi[destinazione.getAttributes().id];
  if (vincolo) {
    // Nodo del template: solo se è uno slot, e solo coi tipi dello slot.
    return !!vincolo.slot && vincolo.slot.tipiAmmessi.includes(tipoFiglio) && destinazione.components().length < vincolo.slot.figliMassimi;
  }
  return destinazione.components().length < (definizione.figliMassimi || 0);
}

export function regolaRadice(editor, vincoli, definizioni) {
  const radice = editor.getWrapper();
  radice.set({
    droppable: (sorgente, destinazione) => consentito(destinazione, sorgente?.get?.('type'), vincoli, definizioni),
    removable: false,
    copyable: false,
    draggable: false,
  });
}

/** Applica i vincoli ai componenti già presenti (progetto caricato). */
export function applicaVincoli(editor, vincoli, definizioni, config) {
  const visita = (componente) => {
    const vincolo = vincoli.nodi[componente.getAttributes().id];
    if (vincolo) {
      componente.set({
        removable: false,
        copyable: false,
        draggable: false,
        // Nessun pulsante di spostamento, copia o eliminazione sui nodi bloccati.
        toolbar: [],
      });
      const modificabili = new Set(vincolo.proprietaModificabili.map((nome) => `data-p-${nome.replace(/[A-Z]/g, (m) => `-${m.toLowerCase()}`)}`));
      componente.set(
        'traits',
        componente.get('traits').models.filter((trait) => modificabili.has(trait.get('name'))).map((trait) => trait.attributes),
      );
      componente.set('name', `${componente.get('name')} 🔒`);
    }
    componente.components().forEach(visita);
  };
  visita(editor.getWrapper());
}

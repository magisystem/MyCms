// Testo con formattazione in markup a grammatica chiusa: stessa grammatica di DvCms.Application.Builder.TestoRicco (C#), verificata con
// gli stessi vettori (test/vettori-testo-ricco.json). Qui serve all'anteprima nel canvas e al controllo immediato nel pannello; l'autorità
// resta il server, che riconverte e valida il markup a ogni salvataggio. Nessun HTML in ingresso: il risultato si costruisce con
// createElement/textContent.

export const LUNGHEZZA_MASSIMA = 5000;
const BLOCCHI_MASSIMI = 60;
const VOCI_MASSIME = 200;
const COLLEGAMENTI_MASSIMI = 50;
const LUNGHEZZA_MASSIMA_URL = 500;
const ESCAPE_AMMESSI = '\\*[]-';

/** Stessa regola di RegistroModelliPagina.UrlConsentito: percorso del sito o https assoluto, senza spazi, controlli o backslash. */
export function urlConsentito(valore) {
  if (/[\s\u0000-\u001f\u007f-\u009f\\]/.test(valore)) return false;
  if (valore.startsWith('/')) return !valore.startsWith('//');
  try {
    const url = new URL(valore);
    return url.protocol === 'https:' && !url.username && !url.password && url.host !== '' && valore.toLowerCase().startsWith('https://');
  } catch {
    return false;
  }
}

function analizzaCollegamento(riga, inizio) {
  let etichetta = '';
  let i = inizio + 1;
  while (i < riga.length && riga[i] !== ']') {
    if (riga[i] === '\\') {
      if (i + 1 >= riga.length || !ESCAPE_AMMESSI.includes(riga[i + 1])) return null;
      etichetta += riga[i + 1];
      i += 2;
    } else if (riga[i] === '*' || riga[i] === '[') {
      return null;
    } else {
      etichetta += riga[i];
      i++;
    }
  }
  if (i + 1 >= riga.length || riga[i] !== ']' || riga[i + 1] !== '(' || etichetta.trim().length === 0) return null;
  const chiusura = riga.indexOf(')', i + 2);
  if (chiusura < 0) return null;
  const url = riga.slice(i + 2, chiusura);
  if (url.length === 0 || url.length > LUNGHEZZA_MASSIMA_URL || url.includes('(') || !urlConsentito(url)) return null;
  return { etichetta, indirizzo: url, fine: chiusura + 1 };
}

function analizzaInline(riga, uscita, stato) {
  let corrente = '';
  let { grassetto, corsivo } = stato;
  const chiudi = () => {
    if (corrente.length > 0) {
      uscita.push({ tipo: 'testo', valore: corrente, grassetto, corsivo });
      corrente = '';
    }
  };
  let i = 0;
  while (i < riga.length) {
    const c = riga[i];
    if (c === '\\') {
      if (i + 1 >= riga.length || !ESCAPE_AMMESSI.includes(riga[i + 1])) return false;
      corrente += riga[i + 1];
      i += 2;
    } else if (c === '*') {
      chiudi();
      if (riga[i + 1] === '*') {
        grassetto = !grassetto;
        i += 2;
      } else {
        corsivo = !corsivo;
        i++;
      }
    } else if (c === '[') {
      const collegamento = analizzaCollegamento(riga, i);
      if (!collegamento) return false;
      chiudi();
      uscita.push({ tipo: 'collegamento', etichetta: collegamento.etichetta, indirizzo: collegamento.indirizzo });
      stato.collegamenti++;
      i = collegamento.fine;
    } else if (c === ']') {
      return false;
    } else {
      corrente += c;
      i++;
    }
  }
  chiudi();
  stato.grassetto = grassetto;
  stato.corsivo = corsivo;
  return true;
}

function unisci(elementi) {
  const risultato = [];
  for (const e of elementi) {
    const ultimo = risultato[risultato.length - 1];
    if (e.tipo === 'testo' && ultimo?.tipo === 'testo' && ultimo.grassetto === e.grassetto && ultimo.corsivo === e.corsivo) {
      ultimo.valore += e.valore;
    } else {
      risultato.push({ ...e });
    }
  }
  return risultato;
}

/** Restituisce i blocchi oppure null se il markup non rispetta la grammatica o i limiti. */
export function analizza(markup) {
  if (typeof markup !== 'string' || markup.trim() === '' || markup.length > LUNGHEZZA_MASSIMA) return null;
  const testo = markup.replace(/\r\n/g, '\n').replace(/^[\n ]+|[\n ]+$/g, '');
  if (/[\u0000-\u0009\u000b-\u001f\u007f-\u009f]/.test(testo)) return null;

  const gruppi = [];
  let corrente = [];
  for (const grezza of testo.split('\n')) {
    const riga = grezza.trim();
    if (riga === '') {
      if (corrente.length) gruppi.push(corrente);
      corrente = [];
    } else {
      corrente.push(riga);
    }
  }
  if (corrente.length) gruppi.push(corrente);

  const blocchi = [];
  let voci = 0;
  const stato = { grassetto: false, corsivo: false, collegamenti: 0 };
  for (const gruppo of gruppi) {
    if (gruppo.every((riga) => riga.startsWith('- '))) {
      const elencoVoci = [];
      for (const riga of gruppo) {
        const contenuto = [];
        stato.grassetto = false;
        stato.corsivo = false;
        if (!analizzaInline(riga.slice(2).trim(), contenuto, stato) || stato.grassetto || stato.corsivo || contenuto.length === 0) return null;
        elencoVoci.push(unisci(contenuto));
      }
      voci += elencoVoci.length;
      blocchi.push({ tipo: 'elenco', voci: elencoVoci });
    } else {
      const contenuto = [];
      stato.grassetto = false;
      stato.corsivo = false;
      for (let i = 0; i < gruppo.length; i++) {
        if (i > 0) contenuto.push({ tipo: 'acapo' });
        if (!analizzaInline(gruppo[i], contenuto, stato)) return null;
      }
      if (stato.grassetto || stato.corsivo) return null;
      blocchi.push({ tipo: 'paragrafo', contenuto: unisci(contenuto) });
    }
    if (blocchi.length > BLOCCHI_MASSIMI || voci > VOCI_MASSIME || stato.collegamenti > COLLEGAMENTI_MASSIMI) return null;
  }
  return blocchi.length ? blocchi : null;
}

function escapeTesto(testo, apreLaRiga) {
  let uscita = '';
  for (let i = 0; i < testo.length; i++) {
    const c = testo[i];
    if (c === '\\' || c === '*' || c === '[' || c === ']') uscita += `\\${c}`;
    else if (c === '-' && i === 0 && apreLaRiga && testo.length > 1 && testo[1] === ' ') uscita += '\\-';
    else uscita += c;
  }
  return uscita;
}

function scriviInline(contenuto, primoDellaRiga) {
  let uscita = '';
  let inizioRiga = primoDellaRiga;
  for (const e of contenuto) {
    if (e.tipo === 'acapo') {
      uscita += '\n';
      inizioRiga = primoDellaRiga;
    } else if (e.tipo === 'collegamento') {
      uscita += `[${escapeTesto(e.etichetta, false)}](${e.indirizzo})`;
      inizioRiga = false;
    } else {
      uscita += `${e.grassetto ? '**' : ''}${e.corsivo ? '*' : ''}${escapeTesto(e.valore, inizioRiga && !e.grassetto && !e.corsivo)}${e.corsivo ? '*' : ''}${e.grassetto ? '**' : ''}`;
      inizioRiga = false;
    }
  }
  return uscita;
}

export function scrivi(blocchi) {
  return blocchi
    .map((b) => (b.tipo === 'elenco' ? b.voci.map((v) => `- ${scriviInline(v, false)}`).join('\n') : scriviInline(b.contenuto, true)))
    .join('\n\n');
}

const codifica = (t) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');

function htmlInline(contenuto) {
  return contenuto
    .map((e) => {
      if (e.tipo === 'acapo') return '<br>';
      if (e.tipo === 'collegamento') return `<a href="${codifica(e.indirizzo)}">${codifica(e.etichetta)}</a>`;
      return `${e.grassetto ? '<strong>' : ''}${e.corsivo ? '<em>' : ''}${codifica(e.valore)}${e.corsivo ? '</em>' : ''}${e.grassetto ? '</strong>' : ''}`;
    })
    .join('');
}

/** HTML di riferimento (come quello dei vettori di prova); si usa nei test, non per inserire nel DOM. */
export function htmlDiRiferimento(blocchi) {
  return blocchi
    .map((b) => (b.tipo === 'elenco' ? `<ul>${b.voci.map((v) => `<li>${htmlInline(v)}</li>`).join('')}</ul>` : `<p>${htmlInline(b.contenuto)}</p>`))
    .join('');
}

/** Costruisce gli elementi nel documento dato (il canvas): solo p, br, strong, em, a, ul, li; testo sempre con textContent. */
export function costruisci(documento, blocchi) {
  const frammento = documento.createDocumentFragment();
  const riempi = (genitore, contenuto) => {
    for (const e of contenuto) {
      if (e.tipo === 'acapo') {
        genitore.append(documento.createElement('br'));
      } else if (e.tipo === 'collegamento') {
        const a = documento.createElement('a');
        a.textContent = e.etichetta;
        a.setAttribute('href', e.indirizzo);
        genitore.append(a);
      } else {
        let nodo = documento.createTextNode(e.valore);
        if (e.corsivo) {
          const em = documento.createElement('em');
          em.append(nodo);
          nodo = em;
        }
        if (e.grassetto) {
          const strong = documento.createElement('strong');
          strong.append(nodo);
          nodo = strong;
        }
        genitore.append(nodo);
      }
    }
  };
  for (const b of blocchi) {
    if (b.tipo === 'elenco') {
      const ul = documento.createElement('ul');
      for (const v of b.voci) {
        const li = documento.createElement('li');
        riempi(li, v);
        ul.append(li);
      }
      frammento.append(ul);
    } else {
      const p = documento.createElement('p');
      riempi(p, b.contenuto);
      frammento.append(p);
    }
  }
  return frammento;
}

// Verifica con un browser vero dell'uso del page builder SOLO DA TASTIERA (M5, accessibilità dell'editor). Come
// percorso-completo.mjs non fa parte della CI: richiede un'istanza in esecuzione con un database popolato e Chromium.
//
// Controlla: collegamenti rapidi, inserimento dei blocchi con Invio, albero della struttura con frecce, spostamento, duplicazione,
// eliminazione, modifica delle proprietà, annullamento, salvataggio con Ctrl+S, e che i nodi bloccati restino intoccabili.
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright');

const base = process.env.DVCMS_URL ?? 'http://localhost:5080';
const email = process.env.DVCMS_EMAIL ?? 'operatore@esempio.it';
const password = process.env.DVCMS_PASSWORD;
if (!password) {
  console.error('Impostare DVCMS_PASSWORD (e, se serve, DVCMS_URL, DVCMS_EMAIL, CHROMIUM_PATH, PLAYWRIGHT_MODULE).');
  process.exit(2);
}

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH, args: ['--no-sandbox'] });
const pagina = await (await browser.newContext({ viewport: { width: 1500, height: 900 } })).newPage();
const problemi = [];
pagina.on('pageerror', (e) => problemi.push(`errore di pagina: ${e.message}`));
pagina.on('console', (m) => m.type() === 'error' && !m.text().includes('Failed to load resource') && problemi.push(`console: ${m.text()}`));
await pagina.addInitScript(() => {
  window.__violazioniCsp = [];
  document.addEventListener('securitypolicyviolation', (e) => window.__violazioniCsp.push(`${e.violatedDirective} ${e.blockedURI}`));
});

const passi = [];
const passo = (nome) => passi.push(nome);

await pagina.goto(`${base}/admin/accesso`);
await pagina.fill('#Email', email);
await pagina.fill('#Password', password);
await Promise.all([pagina.waitForNavigation(), pagina.click('button[type=submit]')]);


const crea = async (modello) => {
  await pagina.goto(`${base}/admin/pagine/nuova?modello=${modello}`);
  await pagina.fill('#Titolo', `Tastiera ${Date.now()}`);
  await Promise.all([pagina.waitForNavigation(), pagina.click('button.btn-primary')]);
  const id = pagina.url().split('/').pop();
  await pagina.goto(`${base}/admin/pagine/${id}/builder`);
  await pagina.waitForFunction(() => window.dvEditor, null, { timeout: 15000 });
  await pagina.locator('.gjs-block[role=button]').first().waitFor();
  await pagina.waitForTimeout(500);
  return id;
};
const albero = () => pagina.evaluate(() => {
  const f = (c) => `${c.get('type').split('.').pop()}${c.components().length ? `[${c.components().map(f).join(',')}]` : ''}`;
  return window.dvEditor.getWrapper().components().map(f).join(' | ');
});
const selezionato = () => pagina.evaluate(() => window.dvEditor.getSelected()?.get('type') ?? null);
const voceAlbero = () => pagina.evaluate(() => [...document.querySelectorAll('.dv-struttura [role=treeitem]')].map((e) => `${e.getAttribute('aria-level')}:${e.getAttribute('aria-label')}${e.getAttribute('aria-selected') === 'true' ? '*' : ''}`));
const focus = () => pagina.evaluate(() => { const a = document.activeElement; return a ? `${a.tagName}|${a.getAttribute('role') ?? ''}|${a.getAttribute('aria-label') ?? a.textContent.trim()}` : ''; });
const premiSuBlocco = async (nome) => {
  const blocco = pagina.locator(`.gjs-block[aria-label="Inserisci ${nome}"]`);
  await blocco.focus();
  await pagina.keyboard.press('Enter');
  await pagina.waitForTimeout(150);
};
const attiva = async (testo) => {
  const pulsante = pagina.locator('.dv-azioni button', { hasText: new RegExp(`^${testo}$`) });
  await pulsante.focus();
  await pagina.keyboard.press('Enter');
  await pagina.waitForTimeout(150);
};

// 1. Pagina libera: collegamenti rapidi e inserimento dei blocchi.
await crea('pagina-libera');
await pagina.keyboard.press('Tab');
assert.match(await focus(), /Vai ai blocchi/, 'il primo Tab porta al collegamento rapido ai blocchi');
await pagina.keyboard.press('Enter');
assert.match(await focus(), /^DIV\|button\|Inserisci /, 'Invio sul collegamento porta al primo blocco');
passo('collegamenti rapidi');

await premiSuBlocco('Sezione');
assert.equal(await selezionato(), 'layout.sezione');
await premiSuBlocco('Titolo');
await premiSuBlocco('Testo');
assert.equal(await albero(), 'sezione[titolo,testo]', 'titolo e testo finiscono nella sezione selezionata, il secondo dopo il primo');
assert.deepEqual(await voceAlbero(), ['1:Sezione', '2:Titolo', '2:Testo*']);
assert.match(await pagina.locator('[role=status][aria-live=polite]').textContent(), /Selezionato: Testo/);
passo('inserimento dei blocchi con Invio');

// Griglia di caratteristiche e testimonianze: nascono con un elemento figlio; gli elementi da soli non sono blocchi.
await premiSuBlocco('Sezione');
await premiSuBlocco('Caratteristiche');
await pagina.locator('.dv-struttura [role=treeitem][aria-selected=true]').focus();
await pagina.keyboard.press('ArrowUp');
await premiSuBlocco('Testimonianze');
assert.match(await albero(), /sezione\[caratteristiche\[caratteristica\],testimonianze\[testimonianza\]\]/);
assert.equal(await pagina.locator('.gjs-block[aria-label="Inserisci Caratteristica"]').count(), 0, 'la caratteristica da sola non è un blocco');
const nelCanvas = await pagina.evaluate(() => {
  const d = window.dvEditor.Canvas.getDocument();
  return [d.querySelector('.dv-caratteristica-titolo')?.textContent, d.querySelector('.dv-testimonianza-autore')?.textContent, d.querySelector('.dv-caratteristiche')?.className];
});
assert.deepEqual(nelCanvas, ['Caratteristica', 'Autore', 'dv-caratteristiche dv-colonne dv-colonne-3']);
passo('caratteristiche e testimonianze nel canvas');
// Si riparte da una pagina pulita per il resto del percorso.
await crea('pagina-libera');
await pagina.keyboard.press('Tab');
await pagina.keyboard.press('Enter');
await premiSuBlocco('Sezione');
await premiSuBlocco('Titolo');
await premiSuBlocco('Testo');

// 2. Albero: frecce, selezione che segue il focus, riduci/espandi.
await pagina.locator('.dv-struttura [role=treeitem][aria-selected=true]').focus();
await pagina.keyboard.press('ArrowUp');
assert.equal(await selezionato(), 'contenuto.titolo');
await pagina.keyboard.press('ArrowUp');
assert.equal(await selezionato(), 'layout.sezione');
await pagina.keyboard.press('ArrowLeft');
assert.deepEqual(await voceAlbero(), ['1:Sezione*'], 'Freccia sinistra riduce il ramo');
await pagina.keyboard.press('ArrowRight');
assert.equal((await voceAlbero()).length, 3, 'Freccia destra lo espande');
await pagina.keyboard.press('ArrowDown');
await pagina.keyboard.press('End');
assert.equal(await selezionato(), 'contenuto.testo');
await pagina.keyboard.press('Home');
assert.equal(await selezionato(), 'layout.sezione');
passo('albero: frecce, Home, Fine, riduci ed espandi');

// 3. Azioni: sposta, duplica, elimina, annulla.
await pagina.keyboard.press('ArrowDown');
assert.equal(await selezionato(), 'contenuto.titolo');
assert.equal(await pagina.locator('.dv-azioni button', { hasText: /^Sposta su$/ }).isDisabled(), true, 'il primo figlio non sale');
await attiva('Sposta giù');
assert.equal(await albero(), 'sezione[testo,titolo]');
assert.match(await pagina.locator('[role=status][aria-live=polite]').textContent(), /spostato in posizione 2 di 2/);
await attiva('Sposta su');
assert.equal(await albero(), 'sezione[titolo,testo]');
await attiva('Duplica');
assert.equal(await albero(), 'sezione[titolo,titolo,testo]');
await attiva('Elimina');
assert.equal(await albero(), 'sezione[titolo,testo]');
await pagina.locator('.dv-barra button', { hasText: /^Annulla$/ }).focus();
await pagina.keyboard.press('Enter');
assert.equal(await albero(), 'sezione[titolo,titolo,testo]', 'Annulla ripristina il componente eliminato');
passo('azioni da tastiera: sposta, duplica, elimina, annulla');

// 4. Proprietà: da un componente della struttura al suo primo campo.
await pagina.locator('.dv-struttura [role=treeitem]', { hasText: 'Testo' }).focus();
await pagina.keyboard.press('End'); // seleziona l'ultima voce (Testo): la selezione segue il focus
assert.equal(await selezionato(), 'contenuto.testo');
await pagina.keyboard.press('Enter');
assert.match(await focus(), /^(TEXTAREA|INPUT|SELECT)/, 'Invio nella struttura porta al primo campo delle proprietà');
const senzaNome = await pagina.evaluate(() => [...document.querySelectorAll('.dv-proprieta input, .dv-proprieta select, .dv-proprieta textarea')].filter((e) => !e.getAttribute('aria-label')).length);
assert.equal(senzaNome, 0, 'ogni campo delle proprietà ha un nome accessibile');
await pagina.keyboard.press('Control+A');
await pagina.keyboard.type('Scritto solo con la tastiera');
await pagina.keyboard.press('Tab');
const testo = await pagina.evaluate(() => window.dvEditor.getSelected().getAttributes()['data-p-testo']);
assert.equal(testo, 'Scritto solo con la tastiera');
passo('proprietà modificate da tastiera');

// Pannello proprietà: gruppi, obbligatorio, contatore, controllo immediato.
await pagina.locator('.dv-struttura [role=treeitem]', { hasText: 'Titolo' }).first().focus();
await pagina.keyboard.press('Home');
await pagina.keyboard.press('ArrowDown');
assert.equal(await selezionato(), 'contenuto.titolo');
await pagina.waitForTimeout(200);
const pannello = await pagina.evaluate(() => ({
  gruppi: [...document.querySelectorAll('.dv-proprieta .dv-gruppo')].map((e) => e.textContent),
  obbligatori: [...document.querySelectorAll('.dv-proprieta [aria-required=true]')].length,
  note: [...document.querySelectorAll('.dv-proprieta .dv-campo-nota')].map((e) => e.textContent),
}));
assert.deepEqual(pannello.gruppi, ['Contenuto', 'Aspetto', 'Animazione']);
assert.equal(pannello.obbligatori, 1, 'il testo del titolo è obbligatorio');
assert.ok(pannello.note.some((n) => /^\d+ \/ 200$/.test(n)), `contatore dei caratteri: ${pannello.note.join('|')}`);
const campoTesto = pagina.locator('.dv-proprieta input[aria-required=true]');
await campoTesto.fill('');
await campoTesto.dispatchEvent('change');
assert.equal(await campoTesto.getAttribute('aria-invalid'), 'true');
assert.match(await pagina.locator('.dv-proprieta .dv-campo-errore').first().textContent(), /Campo obbligatorio/);
await campoTesto.fill('Titolo valido');
await campoTesto.dispatchEvent('change');
assert.equal(await campoTesto.getAttribute('aria-invalid'), 'false');
passo('pannello proprietà: gruppi, campi obbligatori, contatore e controllo immediato');

// 5. Salvataggio da tastiera e nessuna violazione.
const [salvato] = await Promise.all([pagina.waitForResponse((r) => r.url().includes('handler=Salva')), pagina.keyboard.press('Control+S')]);
assert.equal(salvato.status(), 200);
passo('salvataggio con Ctrl+S');

// 6. Template con nodi bloccati: gli strumenti non li toccano.
await crea('pagina-con-intestazione-fissa');
const voci = await voceAlbero();
assert.ok(voci.some((v) => /\(bloccato\)/.test(v)), `la struttura segnala i nodi bloccati: ${voci.join(' / ')}`);
const bloccato = pagina.locator('.dv-struttura [role=treeitem][aria-label*="bloccato"]').first();
await bloccato.focus();
await pagina.keyboard.press('ArrowDown');
await pagina.keyboard.press('ArrowUp'); // la selezione segue il focus: ora è selezionato un nodo bloccato
assert.match(await focus(), /bloccato/);
for (const nome of ['Sposta su', 'Sposta giù', 'Duplica', 'Elimina']) {
  assert.equal(await pagina.locator('.dv-azioni button', { hasText: new RegExp(`^${nome}$`) }).isDisabled(), true, `${nome} è disabilitato sul nodo bloccato`);
}
const prima = await albero();
await pagina.keyboard.press('Delete');
await pagina.keyboard.press('Backspace');
assert.equal(await albero(), prima, 'i tasti Canc e Backspace non cancellano componenti');
passo('nodi bloccati: azioni disabilitate, nessuna scorciatoia che cancella');

const violazioni = await pagina.evaluate(() => window.__violazioniCsp);
assert.deepEqual(violazioni, [], `violazioni della CSP: ${violazioni.join('; ')}`);
assert.deepEqual(problemi, [], problemi.join('\n'));
passo('nessuna violazione della CSP né errore in console');

await browser.close();
console.log(passi.map((p) => `ok - ${p}`).join('\n'));

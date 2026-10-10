// Verifica con un browser vero del percorso completo del page builder (ADR-0007, M4). Non fa parte della CI (richiede
// un'istanza in esecuzione con un database popolato e Chromium): serve a rifare la valutazione del PoC dopo un aggiornamento
// di GrapesJS o dei sorgenti. Vedi README.md per l'uso.
//
// Controlla: il bundle si monta senza violazioni della CSP, i vincoli del template si applicano all'interfaccia, il salvataggio
// funziona e lo stato si ricarica identico, un payload manomesso è rifiutato dal server, il testo ostile resta testo.
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

await pagina.goto(`${base}/admin/pagine/nuova?modello=pagina-con-intestazione-fissa`);
await pagina.fill('#Titolo', `Verifica ${Date.now()}`);
await Promise.all([pagina.waitForNavigation(), pagina.click('button.btn-primary')]);
const id = pagina.url().split('/').pop();
await pagina.goto(`${base}/admin/pagine/${id}/builder`);
await pagina.waitForFunction(() => window.dvEditor, null, { timeout: 15000 });
passo('il bundle si monta');

const albero = () => pagina.evaluate(() => {
  const f = (c) => `${c.getAttributes().id ?? ''}:${c.get('type')}[${c.components().map(f).join(',')}]`;
  return window.dvEditor.getWrapper().components().map(f).join(' | ');
});
const posizione = (selettore) => pagina.evaluate((s) => {
  const el = window.dvEditor.Canvas.getDocument().querySelector(s);
  const r = el.getBoundingClientRect();
  const fr = window.dvEditor.Canvas.getFrameEl().getBoundingClientRect();
  return { x: fr.x + r.x + r.width / 2, y: fr.y + r.y + 8 };
}, selettore);
const trascina = async (nome, selettore) => {
  const b = await pagina.locator('.gjs-block', { hasText: nome }).first().boundingBox();
  const p = await posizione(selettore);
  await pagina.mouse.move(b.x + 20, b.y + 20);
  await pagina.mouse.down();
  await pagina.mouse.move(p.x, p.y, { steps: 15 });
  await pagina.mouse.move(p.x + 3, p.y + 3, { steps: 5 });
  await pagina.mouse.up();
  await pagina.waitForTimeout(400);
};

// Vincoli dell'interfaccia: nello slot sì, una sezione nello slot e un titolo nell'intestazione bloccata no.
const prima = await albero();
await trascina('Testo', '#corpo-testo');
assert.notEqual(await albero(), prima, 'un testo si può aggiungere nello slot');
const conTesto = await albero();
await trascina('Sezione', '#corpo-testo');
await trascina('Titolo', '#intestazione-titolo');
assert.equal(await albero(), conTesto, 'sezioni nello slot e contenuti nell\'intestazione bloccata sono rifiutati dall\'interfaccia');
passo('i vincoli del template guidano l\'interfaccia');

// Proprietà: solo il testo dell'intestazione è modificabile; il testo ostile resta testo.
await pagina.evaluate(() => { window.dvEditor.select(window.dvEditor.getWrapper().find('#intestazione-titolo')[0]); });
assert.deepEqual(await pagina.evaluate(() => window.dvEditor.getSelected().get('traits').models.map((t) => t.get('name'))), ['data-p-testo']);
const ostile = '<img src=x onerror=alert(1)> & "virgolette"';
const campo = pagina.locator('.dv-proprieta input[type=text]').first();
await campo.fill(ostile);
await campo.dispatchEvent('change');
assert.equal(await pagina.evaluate(() => window.dvEditor.Canvas.getDocument().querySelector('#intestazione-titolo').textContent), ostile);
assert.equal(await pagina.evaluate(() => window.dvEditor.Canvas.getDocument().querySelectorAll('img[onerror]').length), 0);
passo('proprietà modificabili solo dove ammesso; il testo ostile non diventa HTML');

// Salvataggio e ricarica.
const [salvato] = await Promise.all([pagina.waitForResponse((r) => r.url().includes('handler=Salva')), pagina.click('.dv-pulsante-salva')]);
assert.equal(salvato.status(), 200);
const progettoSalvato = await pagina.evaluate(() => JSON.stringify(window.dvEditor.getProjectData().pages[0].frames[0].component.components.map((c) => c.attributes?.id)));
await pagina.reload();
await pagina.waitForFunction(() => window.dvEditor, null, { timeout: 15000 });
assert.equal(await pagina.evaluate(() => JSON.stringify(window.dvEditor.getProjectData().pages[0].frames[0].component.components.map((c) => c.attributes?.id))), progettoSalvato);
assert.equal(await pagina.evaluate(() => window.dvEditor.getWrapper().find('#intestazione-titolo')[0].getAttributes()['data-p-testo']), ostile);
passo('salvataggio e ricarica restituiscono lo stesso contenuto');

// Manomissione: si toglie un nodo bloccato aggirando l'interfaccia; il server rifiuta.
await pagina.evaluate(() => { const a = window.dvEditor.getWrapper().find('#avviso')[0]; a.set('removable', true); a.remove(); });
const [rifiutato] = await Promise.all([pagina.waitForResponse((r) => r.url().includes('handler=Salva')), pagina.click('.dv-pulsante-salva')]);
assert.equal(rifiutato.status(), 400);
assert.ok((await rifiutato.json()).errori.some((e) => e.codice === 'NodoBloccatoMancante' && e.idNodo === 'avviso'));
passo('un nodo bloccato rimosso aggirando l\'interfaccia è rifiutato dal server');

const violazioni = await pagina.evaluate(() => window.__violazioniCsp);
assert.deepEqual(violazioni, [], `violazioni della CSP: ${violazioni.join('; ')}`);
assert.deepEqual(problemi, [], problemi.join('\n'));
passo('nessuna violazione della CSP né errore in console');

await browser.close();
console.log(passi.map((p) => `ok - ${p}`).join('\n'));

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
await pagina.frameLocator('iframe.gjs-frame').locator('#corpo-testo').waitFor();
await pagina.waitForTimeout(500);
const temaCanvas = await pagina.evaluate(() => {
  const corpo = window.dvEditor.Canvas.getBody();
  const fogli = [...window.dvEditor.Canvas.getDocument().querySelectorAll('link[rel=stylesheet]')].map((l) => l.getAttribute('href'));
  return { classe: [...corpo.classList].find((c) => c.startsWith('tema-')), fogli };
});
assert.ok(temaCanvas.classe, 'il corpo del canvas porta la classe del tema');
assert.ok(temaCanvas.fogli.some((f) => f.includes('-componenti.css')), 'il canvas carica la skin dei componenti del tema');
passo('il canvas usa tema e skin del sito');

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
// Il primo trascinamento dopo il caricamento può non partire (il canvas sta ancora completando il disegno): si riprova.
for (let tentativo = 0; tentativo < 4 && (await albero()) === prima; tentativo++) {
  await trascina('Testo', '#corpo-testo');
}
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

// Anteprima per dispositivo: la larghezza del canvas cambia.
const larghezzaCanvas = () => pagina.evaluate(() => Math.round(window.dvEditor.Canvas.getFrameEl().getBoundingClientRect().width));
const larghezzaDesktop = await larghezzaCanvas();
await pagina.click('.dv-dispositivo[data-dispositivo="mobile"]');
await pagina.waitForTimeout(400);
const larghezzaMobile = await larghezzaCanvas();
assert.ok(larghezzaMobile <= 380, `il canvas mobile è stretto (${larghezzaMobile}px)`);
await pagina.click('.dv-dispositivo[data-dispositivo="desktop"]');
await pagina.waitForTimeout(400);
assert.equal(await larghezzaCanvas(), larghezzaDesktop);
passo('anteprima per dispositivo');

// Modifica del testo nel canvas: doppio clic, scrittura, conferma; il nodo bloccato accetta solo la proprietà ammessa.
const cornice = pagina.frameLocator('iframe.gjs-frame');
await cornice.locator('#intestazione-titolo').dblclick();
await pagina.keyboard.press('Control+A');
await pagina.keyboard.type('Titolo scritto nel canvas');
await pagina.keyboard.press('Enter');
await pagina.waitForTimeout(200);
assert.equal(await pagina.evaluate(() => window.dvEditor.getWrapper().find('#intestazione-titolo')[0].getAttributes()['data-p-testo']), 'Titolo scritto nel canvas');
await cornice.locator('#avviso-testo').dblclick();
await pagina.keyboard.type('NON DEVE ENTRARE');
await pagina.keyboard.press('Escape');
assert.equal(await pagina.evaluate(() => window.dvEditor.getWrapper().find('#avviso-testo')[0].getAttributes()['data-p-testo']), 'Le informazioni di questa pagina sono fornite a scopo illustrativo.');
passo('modifica del testo nel canvas (solo dove ammesso)');

// Salvataggio automatico: dopo una pausa nelle modifiche il server riceve una revisione marcata come automatica.
const attesaAuto = pagina.waitForResponse((r) => r.url().includes('handler=Salva') && r.request().postDataJSON()?.automatico === true, { timeout: 20000 });
await pagina.evaluate(() => { window.dvEditor.getWrapper().find('#corpo')[0].append({ type: 'contenuto.testo', attributes: { 'data-p-testo': 'Aggiunto e salvato da solo' } }); });
const automatico = await attesaAuto;
assert.equal(automatico.status(), 200);
assert.match(await pagina.textContent('.dv-stato'), /automaticamente/i);
passo('salvataggio automatico dopo una pausa');

// Testo formattato: anteprima con la stessa grammatica del server, barra del markup, controllo immediato.
const idRicco = await pagina.evaluate(() => {
  const e = window.dvEditor;
  const [nuovo] = e.getWrapper().find('#corpo')[0].append({ type: 'contenuto.testo-ricco', attributes: { 'data-p-testo': 'Ciao **mondo**' } });
  e.select(nuovo);
  return nuovo.getAttributes().id;
});
await pagina.waitForTimeout(300);
const ricco = () => pagina.evaluate((id) => {
  const el = window.dvEditor.Canvas.getDocument().getElementById(id);
  return { html: el.innerHTML, nonValido: el.classList.contains('dv-markup-non-valido') };
}, idRicco);
assert.equal((await ricco()).html, '<p>Ciao <strong>mondo</strong></p>');
const area = pagina.locator('.dv-proprieta textarea');
await area.fill('Testo *nuovo* [vai](/dove)\n\n- uno\n- due');
await area.dispatchEvent('change');
assert.equal((await ricco()).html, '<p>Testo <em>nuovo</em> <a href="/dove">vai</a></p><ul><li>uno</li><li>due</li></ul>');
await area.fill('parola');
await area.evaluate((el) => el.setSelectionRange(0, 6));
await pagina.click('.dv-markup-barra button[title="Grassetto"]');
assert.equal(await area.inputValue(), '**parola**');
assert.equal((await ricco()).html, '<p><strong>parola</strong></p>');
await area.fill('**aperto');
await area.dispatchEvent('change');
assert.equal((await ricco()).nonValido, true, 'il markup non valido è evidenziato');
assert.ok(await area.evaluate((el) => el.classList.contains('dv-markup-non-valido')));
await area.fill('<img src=x onerror=alert(1)> ok');
await area.dispatchEvent('change');
assert.equal((await ricco()).html, '<p>&lt;img src=x onerror=alert(1)&gt; ok</p>');
assert.equal(await pagina.evaluate((id) => window.dvEditor.Canvas.getDocument().getElementById(id).querySelectorAll('img').length, idRicco), 0);
passo('testo formattato: anteprima, barra del markup, controllo immediato, nessun HTML');

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

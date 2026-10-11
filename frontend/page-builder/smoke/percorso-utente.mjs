// Percorso utente completo del page builder (M5, exit criteria): da una pagina nuova, con un template guidato e con la pagina libera,
// fino alla pubblicazione e alla lettura della pagina da un visitatore anonimo. Solo interfaccia (clic, campi, pulsanti), come
// farebbe una persona; richiede un'istanza in esecuzione con un database popolato e Chromium (vedi README.md).
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


const anonimo = await (await browser.newContext()).newPage();
const marca = Date.now();

const apriBuilder = async (modello, slug) => {
  await pagina.goto(`${base}/admin/pagine/nuova?modello=${modello}`);
  await pagina.fill('#Titolo', `Percorso ${slug}`);
  await pagina.fill('#Slug', slug);
  await Promise.all([pagina.waitForNavigation(), pagina.click('button.btn-primary')]);
  await Promise.all([pagina.waitForNavigation(), pagina.click('a:has-text("page builder")')]);
  await pagina.waitForFunction(() => window.dvEditor, null, { timeout: 15000 });
  await pagina.locator('.gjs-block[role=button]').first().waitFor();
  await pagina.waitForTimeout(500);
};
const blocco = async (nome) => {
  await pagina.locator(`.gjs-block[aria-label="Inserisci ${nome}"]`).click();
  await pagina.waitForTimeout(200);
};
const voce = (testo, indice = 0) => pagina.locator('.dv-struttura [role=treeitem]').filter({ hasText: testo }).nth(indice);
const campo = (nome) => pagina.locator('.dv-proprieta .gjs-trt-trait').filter({ has: pagina.locator('.gjs-label', { hasText: new RegExp(`^${nome}`) }) }).locator('input, textarea').first();
const scrivi = async (nome, testo) => {
  const c = campo(nome);
  await c.fill(testo);
  await c.dispatchEvent('change');
};
const salvaEPubblica = async (slug) => {
  await pagina.click('.dv-pulsante-salva');
  await pagina.locator('.dv-stato[data-tipo=ok]').waitFor({ timeout: 10000 });
  await Promise.all([pagina.waitForNavigation(), pagina.click('a:has-text("Torna alla pagina")')]);
  await Promise.all([pagina.waitForNavigation(), pagina.click('button:has-text("Pubblica l\'ultima bozza")')]);
  const risposta = await anonimo.goto(`${base}/${slug}`);
  assert.equal(risposta.status(), 200, `la pagina /${slug} è pubblica`);
  return anonimo.content();
};

// A. Template guidato: si cambia solo ciò che il template consente e si aggiunge nello spazio previsto.
const slugGuidato = `guidato-${marca}`;
await apriBuilder('pagina-con-intestazione-fissa', slugGuidato);
await voce('Titolo').click();
await scrivi('Testo', 'Benvenuti nel percorso guidato');
await voce('Sezione', 1).click(); // il corpo (slot)
await blocco('Caratteristiche');
await voce('Caratteristica').click();
await scrivi('Titolo', 'Veloce da usare');
await scrivi('Descrizione', 'Si pubblica in pochi minuti.');
await voce('Sezione', 1).click();
await blocco('Pulsante');
const htmlGuidato = await salvaEPubblica(slugGuidato);
assert.match(htmlGuidato, /Benvenuti nel percorso guidato/);
assert.match(htmlGuidato, /dv-caratteristiche/);
assert.match(htmlGuidato, /Veloce da usare/);
assert.match(htmlGuidato, /Si pubblica in pochi minuti\./);
assert.match(htmlGuidato, /Scrivi qui il contenuto della pagina\./, 'il testo del template resta');
assert.match(htmlGuidato, /Le informazioni di questa pagina sono fornite a scopo illustrativo\./, 'la parte bloccata resta');
assert.match(htmlGuidato, /class="[^"]*dv-pulsante/);
assert.doesNotMatch(htmlGuidato, /<script>alert/);
passo('template guidato: modifica, aggiunta nello slot, salvataggio, pubblicazione, lettura da visitatore');

// B. Pagina libera: tutto si costruisce da zero.
const slugLibero = `libero-${marca}`;
await apriBuilder('pagina-libera', slugLibero);
await blocco('Sezione');
await blocco('Hero');
await scrivi('Titolo', 'Una pagina tutta mia');
await scrivi('Sottotitolo', 'Costruita da zero');
await voce('Sezione').click();
await blocco('Testimonianze');
await voce('Testimonianza').click();
await scrivi('Citazione', 'Ho pubblicato senza chiedere aiuto.');
await scrivi('Autore', 'Giulia');
await scrivi('Ruolo', 'Redattrice');
await voce('Sezione').click();
await blocco('Fisarmonica');
await voce('Voce').click();
await scrivi('Domanda', 'Come funziona?');
await scrivi('Risposta', 'Si scrive, si salva, si pubblica.');
const htmlLibero = await salvaEPubblica(slugLibero);
assert.match(htmlLibero, /Una pagina tutta mia/);
assert.match(htmlLibero, /Costruita da zero/);
assert.match(htmlLibero, /Ho pubblicato senza chiedere aiuto\./);
assert.match(htmlLibero, /<strong>Giulia<\/strong>, Redattrice/);
assert.match(htmlLibero, /<summary>Come funziona\?<\/summary>/);
passo('pagina libera: costruzione da zero con hero, testimonianze e fisarmonica fino alla pubblicazione');

const violazioni = await pagina.evaluate(() => window.__violazioniCsp);
assert.deepEqual(violazioni, [], `violazioni della CSP: ${violazioni.join('; ')}`);
assert.deepEqual(problemi, [], problemi.join('\n'));
passo('nessuna violazione della CSP né errore in console');

await browser.close();
console.log(passi.map((p) => `ok - ${p}`).join('\n'));

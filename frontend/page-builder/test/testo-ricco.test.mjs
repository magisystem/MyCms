// Verifica la grammatica del testo con formattazione dell'editor con gli stessi vettori usati dai test .NET (test/vettori-testo-ricco.json).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { analizza, scrivi, htmlDiRiferimento } from '../src/testoRicco.js';

const vettori = JSON.parse(readFileSync(new URL('./vettori-testo-ricco.json', import.meta.url), 'utf8'));

for (const [indice, vettore] of vettori.entries()) {
  test(`vettore ${indice}: ${JSON.stringify(vettore.input).slice(0, 50)}`, () => {
    const blocchi = analizza(vettore.input);
    if (vettore.errore) {
      assert.equal(blocchi, null);
      return;
    }
    assert.notEqual(blocchi, null);
    assert.equal(scrivi(blocchi), vettore.canonico);
    assert.equal(htmlDiRiferimento(blocchi), vettore.html);
    assert.equal(scrivi(analizza(vettore.canonico)), vettore.canonico);
  });
}

test('limiti', () => {
  assert.equal(analizza('a'.repeat(5001)), null);
  assert.equal(analizza(Array(61).fill('a').join('\n\n')), null);
  assert.notEqual(analizza(Array(60).fill('a').join('\n\n')), null);
  assert.equal(analizza(Array(51).fill('[a](/x)').join(' ')), null);
  assert.equal(analizza(Array(201).fill('- v').join('\n')), null);
});

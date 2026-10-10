// Compila il bundle isolato del page builder (ADR-0007) in src/DvCms.Web/wwwroot/lib/page-builder.
// Il risultato si versiona: in produzione non serve Node. Con --verifica ricompila in memoria e confronta con i file presenti
// (la CI lo usa per assicurarsi che il bundle versionato corrisponda ai sorgenti e al lockfile).
import { build } from 'esbuild';
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const qui = dirname(fileURLToPath(import.meta.url));
const uscita = resolve(qui, '../../src/DvCms.Web/wwwroot/lib/page-builder');
const verifica = process.argv.includes('--verifica');

const opzioni = {
  entryPoints: { 'page-builder': join(qui, 'src/main.js') },
  bundle: true,
  format: 'iife',
  target: ['es2022'],
  minify: true,
  sourcemap: false,
  legalComments: 'none',
  write: false,
  outdir: uscita,
  loader: { '.css': 'css' },
  logLevel: 'warning',
};

const risultato = await build(opzioni);
const file = Object.fromEntries(risultato.outputFiles.map((f) => [f.path.slice(uscita.length + 1), Buffer.from(f.contents)]));

// Elenco delle licenze di terze parti incluse nel bundle (nome, versione, licenza dai package.json installati).
const lock = JSON.parse(readFileSync(join(qui, 'package-lock.json'), 'utf8'));
const righe = ['# Componenti di terze parti incluse in page-builder.js', ''];
for (const [percorso, info] of Object.entries(lock.packages).sort(([a], [b]) => a.localeCompare(b))) {
  if (!percorso.startsWith('node_modules/') || info.dev || percorso.startsWith('node_modules/@types/')) continue;
  righe.push(`${percorso.slice('node_modules/'.length)}@${info.version}  ${info.license ?? 'licenza non dichiarata'}`);
}
righe.push('');
file['LICENZE-TERZE.txt'] = Buffer.from(righe.join('\n'));
// Foglio di stile caricato nel canvas (iframe) dell'editor: file statico a parte, non fa parte del bundle.
file['canvas.css'] = readFileSync(join(qui, 'src/canvas.css'));

if (verifica) {
  let diverso = false;
  for (const [nome, contenuto] of Object.entries(file)) {
    const percorso = join(uscita, nome);
    if (!existsSync(percorso) || !readFileSync(percorso).equals(contenuto)) {
      console.error(`Bundle non aggiornato: ${nome}. Eseguire "npm run build" in frontend/page-builder e versionare il risultato.`);
      diverso = true;
    }
  }
  process.exit(diverso ? 1 : 0);
}

mkdirSync(uscita, { recursive: true });
for (const [nome, contenuto] of Object.entries(file)) {
  writeFileSync(join(uscita, nome), contenuto);
  console.log(`${nome}: ${contenuto.length} byte`);
}

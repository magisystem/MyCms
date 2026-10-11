// Baseline di prestazioni (M6): misura la latenza di alcuni URL di un'istanza in esecuzione, in sequenza e con una concorrenza
// fissa, e stampa percentili e richieste al secondo. Non ha dipendenze (solo Node 22). Non è un test di carico né un criterio di
// accettazione: serve a confrontare lo stesso ambiente prima e dopo una modifica (vedi docs/operations/prestazioni.md).
//
// Uso: node tools/baseline-prestazioni.mjs <base> <percorso>... [--richieste=300] [--concorrenza=8] [--riscaldamento=20]
// Esempio: node tools/baseline-prestazioni.mjs http://localhost:5080 /guidato /sitemap.xml /health/ready /media/<id>
const argomenti = process.argv.slice(2);
const opzioni = Object.fromEntries(argomenti.filter((a) => a.startsWith('--')).map((a) => a.slice(2).split('=')));
const posizionali = argomenti.filter((a) => !a.startsWith('--'));
const [base, ...percorsi] = posizionali;
if (!base || percorsi.length === 0) {
  console.error('Uso: node tools/baseline-prestazioni.mjs <base> <percorso>... [--richieste=300] [--concorrenza=8] [--riscaldamento=20]');
  process.exit(2);
}
const richieste = Number(opzioni.richieste ?? 300);
const concorrenza = Number(opzioni.concorrenza ?? 8);
const riscaldamento = Number(opzioni.riscaldamento ?? 20);

const percentile = (ordinati, p) => ordinati[Math.min(ordinati.length - 1, Math.floor((p / 100) * ordinati.length))];

async function una(url) {
  const inizio = performance.now();
  const risposta = await fetch(url, { redirect: 'manual' });
  const corpo = await risposta.arrayBuffer();
  return { ms: performance.now() - inizio, stato: risposta.status, byte: corpo.byteLength };
}

async function misura(url, n, c) {
  const durate = [];
  const stati = new Map();
  let byte = 0;
  let prossima = 0;
  const inizio = performance.now();
  await Promise.all(Array.from({ length: c }, async () => {
    while (prossima < n) {
      prossima++;
      const r = await una(url);
      durate.push(r.ms);
      byte = r.byte;
      stati.set(r.stato, (stati.get(r.stato) ?? 0) + 1);
    }
  }));
  const secondi = (performance.now() - inizio) / 1000;
  durate.sort((a, b) => a - b);
  return {
    n,
    c,
    rps: Math.round(n / secondi),
    p50: percentile(durate, 50),
    p95: percentile(durate, 95),
    p99: percentile(durate, 99),
    max: durate[durate.length - 1],
    stati: [...stati].map(([s, k]) => `${s}×${k}`).join(' '),
    byte,
  };
}

const f = (x) => x.toFixed(1).padStart(7);
console.log(`Base ${base} — ${richieste} richieste per URL, riscaldamento ${riscaldamento}`);
console.log('percorso'.padEnd(34), 'modo'.padEnd(11), 'rps'.padStart(5), '    p50    p95    p99    max', ' byte  stati');
for (const percorso of percorsi) {
  const url = new URL(percorso, base).href;
  for (let i = 0; i < riscaldamento; i++) await una(url);
  for (const [modo, c] of [['sequenza', 1], [`conc. ${concorrenza}`, concorrenza]]) {
    const r = await misura(url, richieste, c);
    console.log(percorso.slice(0, 33).padEnd(34), modo.padEnd(11), String(r.rps).padStart(5), f(r.p50), f(r.p95), f(r.p99), f(r.max), String(r.byte).padStart(6), ' ', r.stati);
  }
}

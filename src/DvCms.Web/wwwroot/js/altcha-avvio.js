// Avvio del widget ALTCHA (ADR-0026). La versione "external" non include i worker: si registrano qui, da file della stessa origine
// (la CSP non ammette worker da blob). Il server emette solo sfide PBKDF2/SHA-256; le altre due varianti restano per sicurezza.
['PBKDF2/SHA-256', 'PBKDF2/SHA-384', 'PBKDF2/SHA-512'].forEach(function (algoritmo) {
    globalThis.$altcha.algorithms.set(algoritmo, function () {
        return new Worker('/lib/altcha/workers/pbkdf2.js');
    });
});

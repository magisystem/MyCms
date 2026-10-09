// Miglioria progressiva: un <select data-auto-invio> invia il suo modulo (di tipo GET) quando cambia.
// Senza JavaScript resta il pulsante accanto al selettore.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('select[data-auto-invio]').forEach(function (selettore) {
        selettore.addEventListener('change', function () {
            if (selettore.form) {
                selettore.form.submit();
            }
        });
    });
});

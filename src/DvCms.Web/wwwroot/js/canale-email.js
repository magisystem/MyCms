// Miglioria progressiva: cambiando il canale email la pagina si ricarica mostrando i campi di quel canale.
// Senza JavaScript resta il pulsante "Mostra i campi".
document.addEventListener('DOMContentLoaded', function () {
    var selettore = document.getElementById('Canale');
    if (selettore && selettore.form) {
        selettore.addEventListener('change', function () {
            selettore.form.submit();
        });
    }
});

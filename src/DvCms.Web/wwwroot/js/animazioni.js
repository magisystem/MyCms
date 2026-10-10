// Animazioni di comparsa dichiarative delle pagine del builder (E-006). Script fidato del sito: non esegue nulla che
// provenga dal documento, legge solo le classi .dv-anim assegnate dal renderer. Senza IntersectionObserver o con
// prefers-reduced-motion i contenuti restano semplicemente visibili.
(function () {
  'use strict';
  var radice = document.documentElement;
  var ridotto = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (ridotto || !('IntersectionObserver' in window)) {
    return;
  }

  radice.classList.add('dv-js');
  document.addEventListener('DOMContentLoaded', function () {
    var osservatore = new IntersectionObserver(function (voci) {
      voci.forEach(function (voce) {
        if (voce.isIntersecting) {
          voce.target.classList.add('dv-visibile');
          osservatore.unobserve(voce.target);
        }
      });
    }, { threshold: 0.15 });
    document.querySelectorAll('.dv-anim').forEach(function (elemento) {
      osservatore.observe(elemento);
    });
  });
})();

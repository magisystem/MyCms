using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Moduli;
using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Moduli;
using DvCms.Web.AntiAbuso;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Moduli;

/// <summary>Elenco dei moduli di contatto del sito (ADR-0027). Solo il <c>TenantAdmin</c> (politica della cartella e riverifica nel servizio).</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PaginaAdmin
{
    private readonly IServizioModuli _moduli;
    private readonly IVerificaAntiAbuso _antiAbuso;

    public IndexModel(IServizioModuli moduli, IVerificaAntiAbuso antiAbuso, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _moduli = moduli;
        _antiAbuso = antiAbuso;
    }

    public IReadOnlyList<RiepilogoModulo> Elenco { get; private set; } = [];

    /// <summary>Falso se la protezione ALTCHA è spenta: i moduli non accettano invii (ADR-0027, D6).</summary>
    public bool ProtezioneAttiva => _antiAbuso.Attiva;

    public int Massimo => Modulo.NumeroMassimoPerSito;

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        try
        {
            Elenco = await _moduli.ElencaAsync(IdAttore, annullamento);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return Page();
    }

    public Task<IActionResult> OnPostPubblicaAsync(Guid id, string? token, CancellationToken annullamento) =>
        CambiaStatoAsync(id, StatoModulo.Attivo, token, Localizzatore.Traduci("Modulo pubblicato."), annullamento);

    public Task<IActionResult> OnPostBozzaAsync(Guid id, string? token, CancellationToken annullamento) =>
        CambiaStatoAsync(id, StatoModulo.Bozza, token, Localizzatore.Traduci("Modulo riportato in bozza: non accetta più invii."), annullamento);

    public Task<IActionResult> OnPostArchiviaAsync(Guid id, string? token, CancellationToken annullamento) =>
        CambiaStatoAsync(id, StatoModulo.Archiviato, token, Localizzatore.Traduci("Modulo archiviato: non accetta più invii."), annullamento);

    public async Task<IActionResult> OnPostEliminaAsync(Guid id, CancellationToken annullamento)
    {
        var esito = await _moduli.EliminaAsync(IdAttore, id, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Modulo eliminato.");
        }
        else
        {
            MessaggioErrore = esito.Codice == CodiceEsito.NonConsentito
                ? Localizzatore.Traduci("Il modulo ha richieste ricevute: archivialo, le richieste si cancelleranno alla scadenza.")
                : TestoErrore(esito.Codice);
        }

        return RedirectToPage();
    }

    private async Task<IActionResult> CambiaStatoAsync(Guid id, StatoModulo stato, string? token, string messaggio, CancellationToken annullamento)
    {
        var esito = await _moduli.ImpostaStatoAsync(IdAttore, id, stato, token, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = messaggio;
        }
        else
        {
            MessaggioErrore = esito.Errori.Count > 0
                ? string.Join(" ", esito.Errori)
                : TestoErrore(esito.Codice);
        }

        return RedirectToPage();
    }
}

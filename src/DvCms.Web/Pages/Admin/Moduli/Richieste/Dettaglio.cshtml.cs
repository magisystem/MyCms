using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Moduli;
using DvCms.DTO.Moduli;
using DvCms.Models.Moduli;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Moduli.Richieste;

/// <summary>Una richiesta ricevuta (ADR-0027): valori inviati, segna come letta, archivia, elimina. Solo il <c>TenantAdmin</c>.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DettaglioModel : PaginaAdmin
{
    private readonly IServizioRichiesteModulo _richieste;

    public DettaglioModel(IServizioRichiesteModulo richieste, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _richieste = richieste;
    }

    public VistaRichiesta Richiesta { get; private set; } = null!;

    public string NomeStato(StatoInvioModulo stato) => stato switch
    {
        StatoInvioModulo.Nuovo => Localizzatore.Traduci("Nuova"),
        StatoInvioModulo.Letto => Localizzatore.Traduci("Letta"),
        _ => Localizzatore.Traduci("Archiviata"),
    };

    public async Task<IActionResult> OnGetAsync(Guid id, Guid idRichiesta, CancellationToken annullamento)
    {
        try
        {
            var richiesta = await _richieste.LeggiAsync(IdAttore, id, idRichiesta, annullamento);
            if (richiesta is null)
            {
                return NotFound();
            }

            Richiesta = richiesta;
            return Page();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    public Task<IActionResult> OnPostLettaAsync(Guid id, Guid idRichiesta, CancellationToken annullamento) =>
        CambiaStatoAsync(id, idRichiesta, StatoInvioModulo.Letto, Localizzatore.Traduci("Richiesta segnata come letta."), annullamento);

    public Task<IActionResult> OnPostNuovaAsync(Guid id, Guid idRichiesta, CancellationToken annullamento) =>
        CambiaStatoAsync(id, idRichiesta, StatoInvioModulo.Nuovo, Localizzatore.Traduci("Richiesta segnata come nuova."), annullamento);

    public Task<IActionResult> OnPostArchiviaAsync(Guid id, Guid idRichiesta, CancellationToken annullamento) =>
        CambiaStatoAsync(id, idRichiesta, StatoInvioModulo.Archiviato, Localizzatore.Traduci("Richiesta archiviata."), annullamento);

    public async Task<IActionResult> OnPostEliminaAsync(Guid id, Guid idRichiesta, CancellationToken annullamento)
    {
        var esito = await _richieste.EliminaAsync(IdAttore, id, idRichiesta, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Richiesta eliminata.");
            return Redirect($"/admin/moduli/richieste/{id:D}");
        }

        MessaggioErrore = TestoErrore(esito.Codice);
        return RedirectToPage(new { id, idRichiesta });
    }

    private async Task<IActionResult> CambiaStatoAsync(Guid id, Guid idRichiesta, StatoInvioModulo stato, string messaggio, CancellationToken annullamento)
    {
        var esito = await _richieste.ImpostaStatoAsync(IdAttore, id, idRichiesta, stato, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = messaggio;
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id, idRichiesta });
    }
}

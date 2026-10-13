using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Outbox;
using DvCms.Core.Localizzazione;
using DvCms.DTO.Outbox;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Outbox;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Areas.Platform.Pages.Coda;

/// <summary>Stato della coda dell'outbox (G-001): riepilogo, messaggi con l'ultimo errore, ripresa e scarto dei falliti.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PaginaPiattaforma
{
    private const int MessaggiMostrati = 100;

    private readonly IGestioneCodaOutbox _coda;

    public IndexModel(IGestioneCodaOutbox coda, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _coda = coda;
    }

    /// <summary>Filtro per stato (<c>Pendente</c>, <c>Fallito</c>, <c>Completato</c>); vuoto = i falliti, che sono ciò che serve guardare.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Stato { get; set; }

    public RiepilogoCoda Riepilogo { get; private set; } = new(0, 0, 0, null);

    public IReadOnlyList<MessaggioInCoda> Messaggi { get; private set; } = [];

    public StatoMessaggioOutbox StatoMostrato { get; private set; } = StatoMessaggioOutbox.Fallito;

    public async Task OnGetAsync(CancellationToken annullamento)
    {
        StatoMostrato = Enum.TryParse<StatoMessaggioOutbox>(Stato, ignoreCase: true, out var stato) && Enum.IsDefined(stato) ? stato : StatoMessaggioOutbox.Fallito;
        Riepilogo = await _coda.RiepilogoAsync(IdAttore, annullamento);
        Messaggi = await _coda.ElencaAsync(IdAttore, StatoMostrato, MessaggiMostrati, annullamento);
    }

    public Task<IActionResult> OnPostRitentaAsync(Guid id, CancellationToken annullamento) =>
        EseguiAsync(async () => await _coda.RitentaAsync(IdAttore, id, annullamento), Localizzatore.Traduci("Messaggio rimesso in coda."));

    public Task<IActionResult> OnPostScartaAsync(Guid id, CancellationToken annullamento) =>
        EseguiAsync(async () => await _coda.ScartaAsync(IdAttore, id, annullamento), Localizzatore.Traduci("Messaggio scartato."));

    private async Task<IActionResult> EseguiAsync(Func<Task<DvCms.DTO.Piattaforma.EsitoOperazione>> operazione, string messaggioRiuscito)
    {
        var esito = await operazione();
        if (esito.Riuscito)
        {
            Messaggio = messaggioRiuscito;
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { stato = Stato });
    }
}

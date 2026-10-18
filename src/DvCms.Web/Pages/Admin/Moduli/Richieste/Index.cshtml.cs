using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Moduli;
using DvCms.DTO.Moduli;
using DvCms.Models.Moduli;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Moduli.Richieste;

/// <summary>Casella delle richieste di un modulo (ADR-0027): elenco con filtro per stato, esportazione CSV. Solo il <c>TenantAdmin</c>.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PaginaAdmin
{
    private readonly IServizioRichiesteModulo _richieste;

    public IndexModel(IServizioRichiesteModulo richieste, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _richieste = richieste;
    }

    public ElencoRichieste Elenco { get; private set; } = null!;

    public static IReadOnlyList<StatoInvioModulo> Stati { get; } = Enum.GetValues<StatoInvioModulo>();

    public string NomeStato(StatoInvioModulo stato) => stato switch
    {
        StatoInvioModulo.Nuovo => Localizzatore.Traduci("Nuova"),
        StatoInvioModulo.Letto => Localizzatore.Traduci("Letta"),
        _ => Localizzatore.Traduci("Archiviata"),
    };

    public async Task<IActionResult> OnGetAsync(Guid id, string? stato, int? pagina, CancellationToken annullamento)
    {
        try
        {
            var elenco = await _richieste.ElencaAsync(IdAttore, id, Filtro(stato), pagina ?? 1, annullamento);
            if (elenco is null)
            {
                return NotFound();
            }

            Elenco = elenco;
            return Page();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Scarica il CSV delle richieste (con il filtro per stato, se presente). Ogni esportazione è nell'audit.</summary>
    public async Task<IActionResult> OnGetEsportaAsync(Guid id, string? stato, CancellationToken annullamento)
    {
        try
        {
            var esportazione = await _richieste.EsportaAsync(IdAttore, id, Filtro(stato), annullamento);
            if (esportazione is null)
            {
                return NotFound();
            }

            if (esportazione.Troncata)
            {
                Response.Headers["X-Esportazione-Troncata"] = "true";
            }

            return File(esportazione.Contenuto, "text/csv; charset=utf-8", esportazione.NomeFile);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static StatoInvioModulo? Filtro(string? stato) =>
        Enum.TryParse<StatoInvioModulo>(stato, ignoreCase: true, out var valore) && Enum.IsDefined(valore) ? valore : null;
}

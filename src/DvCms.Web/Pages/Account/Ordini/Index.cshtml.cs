using System.Globalization;
using System.Security.Claims;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account.Ordini;

/// <summary>Elenco degli ordini dell'acquirente autenticato (F-003): solo i propri.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private readonly IServizioOrdini _ordini;

    public IndexModel(IServizioOrdini ordini, ILocalizzatore localizzatore)
    {
        _ordini = ordini;
        Localizzatore = localizzatore;
    }

    public ILocalizzatore Localizzatore { get; }

    public IReadOnlyList<RiepilogoOrdine> Ordini { get; private set; } = [];

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    private Guid IdAccount => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public async Task OnGetAsync(CancellationToken annullamento) =>
        Ordini = await _ordini.ElencaMieiAsync(IdAccount, annullamento);
}

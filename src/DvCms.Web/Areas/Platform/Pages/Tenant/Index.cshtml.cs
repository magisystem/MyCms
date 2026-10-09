using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;

namespace DvCms.Web.Areas.Platform.Pages.Tenant;

public class IndexModel : PaginaPiattaforma
{
    private readonly IServizioPiattaforma _piattaforma;

    public IndexModel(IServizioPiattaforma piattaforma, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _piattaforma = piattaforma;
    }

    public IReadOnlyList<RiepilogoTenant> Elenco { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken annullamento)
    {
        Elenco = await _piattaforma.ElencaTenantAsync(IdAttore, annullamento);
    }
}

using DvCms.Contracts.Cms;
using DvCms.Contracts.Localizzazione;
using DvCms.DTO.Cms;

namespace DvCms.Web.Pages.Admin.Pagine;

public class IndexModel : PaginaAdmin
{
    private readonly IServizioPagine _pagine;

    public IndexModel(IServizioPagine pagine, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagine = pagine;
    }

    public IReadOnlyList<RiepilogoPagina> Pagine { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken annullamento)
    {
        Pagine = await _pagine.ElencaAsync(IdAttore, annullamento);
    }
}

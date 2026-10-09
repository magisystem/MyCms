using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;

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

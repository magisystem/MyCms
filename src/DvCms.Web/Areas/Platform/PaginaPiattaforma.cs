using DvCms.Contracts.Localizzazione;

namespace DvCms.Web.Areas.Platform;

/// <summary>Base delle pagine del menu "Platform".</summary>
public abstract class PaginaPiattaforma : PaginaAdmin
{
    protected PaginaPiattaforma(ILocalizzatore localizzatore)
        : base(localizzatore)
    {
    }
}

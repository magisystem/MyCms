using DvCms.Application.Localizzazione;
using DvCms.Models.Localizzazione;

namespace DvCms.Web.Localizzazione;

/// <summary>
/// Lingua della richiesta corrente. Qualunque componente (preferenza dell'account nell'admin, prefisso di lingua
/// del sito pubblico) la imposta in <see cref="HttpContext.Items"/> con la chiave <see cref="ChiaveElemento"/>;
/// senza indicazione vale l'italiano, lingua di base.
/// </summary>
public sealed class ContestoLinguaRichiesta : IContestoLingua
{
    public const string ChiaveElemento = "DvCms:CodiceLingua";

    private readonly IHttpContextAccessor _accessoContesto;

    public ContestoLinguaRichiesta(IHttpContextAccessor accessoContesto)
    {
        _accessoContesto = accessoContesto;
    }

    public string CodiceLingua =>
        _accessoContesto.HttpContext?.Items[ChiaveElemento] as string ?? Lingua.CodiceItaliano;
}

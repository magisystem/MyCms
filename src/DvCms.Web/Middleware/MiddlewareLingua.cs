using System.Security.Claims;
using DvCms.Web.Accessi;
using DvCms.Web.Localizzazione;

namespace DvCms.Web.Middleware;

/// <summary>
/// Imposta la lingua della richiesta. Nell'admin è la preferenza dell'account (claim); senza indicazione vale
/// l'italiano. La lingua del sito pubblico (prefisso del percorso) arriva con C-008.
/// </summary>
public sealed class MiddlewareLingua
{
    private readonly RequestDelegate _successivo;

    public MiddlewareLingua(RequestDelegate successivo)
    {
        _successivo = successivo;
    }

    public Task InvokeAsync(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var codice = contesto.User.FindFirstValue(TipiClaim.CodiceLingua);
        if (!string.IsNullOrWhiteSpace(codice))
        {
            contesto.Items[ContestoLinguaRichiesta.ChiaveElemento] = codice;
        }

        return _successivo(contesto);
    }
}

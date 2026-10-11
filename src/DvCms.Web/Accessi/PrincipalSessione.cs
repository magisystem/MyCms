using System.Globalization;
using System.Security.Claims;
using DvCms.Application.Accessi;

namespace DvCms.Web.Accessi;

public static class PrincipalSessione
{
    /// <summary>Crea il principal della sessione admin per l'identità indicata, sull'host del tenant indicato.</summary>
    public static ClaimsPrincipal Crea(IdentitaUtenza identita, Guid idTenantHost) => Crea(identita, idTenantHost, Schemi.Admin);

    /// <summary>Come <see cref="Crea(IdentitaUtenza, Guid)"/> ma per lo schema indicato (area admin o area cliente).</summary>
    public static ClaimsPrincipal Crea(IdentitaUtenza identita, Guid idTenantHost, string schema)
    {
        ArgumentNullException.ThrowIfNull(identita);

        var claim = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identita.IdUtenza.ToString()),
            new(ClaimTypes.Name, identita.NomeVisualizzato),
            new(ClaimTypes.Email, identita.Email),
            new(ClaimTypes.Role, identita.Ruolo.ToString()),
            new(TipiClaim.IdTenant, idTenantHost.ToString()),
            new(TipiClaim.TimbroSicurezza, identita.TimbroSicurezza.ToString()),
            new(TipiClaim.DeveCambiarePassword, identita.DeveCambiarePassword.ToString(CultureInfo.InvariantCulture)),
            new(TipiClaim.CodiceLingua, identita.CodiceLingua),
            new(TipiClaim.DeveAttivareDueFattori, identita.DeveAttivareDueFattori.ToString(CultureInfo.InvariantCulture)),
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claim, schema, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static bool TentaLeggiSessione(ClaimsPrincipal principal, out Guid idUtenza, out Guid timbro)
    {
        ArgumentNullException.ThrowIfNull(principal);

        idUtenza = Guid.Empty;
        timbro = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out idUtenza)
            && Guid.TryParse(principal.FindFirstValue(TipiClaim.TimbroSicurezza), out timbro);
    }

    public static bool DeveCambiarePassword(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return string.Equals(principal.FindFirstValue(TipiClaim.DeveCambiarePassword), bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }

    public static bool DeveAttivareDueFattori(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return string.Equals(principal.FindFirstValue(TipiClaim.DeveAttivareDueFattori), bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }
}

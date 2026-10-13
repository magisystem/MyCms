using System.Text.Json;
using DvCms.DTO.Accessi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Accessi;

/// <summary>Contenuto della sfida del secondo fattore, letta dal cookie dopo la verifica della password.</summary>
public sealed record ContenutoSfida(Guid IdUtenza, Guid TimbroSicurezza, string? Destinazione);

/// <summary>
/// Cookie di sfida del secondo fattore (ADR-0019): nasce dopo la password corretta, vale pochi minuti ed è solo la promessa
/// "questa persona ha già superato la password" — non è una sessione e non dà accesso a nessuna pagina. È cifrato e autenticato con
/// Data Protection, legato al tenant dell'host (non vale su un altro host), <c>HttpOnly</c> e <c>SameSite=Strict</c>. La sessione vera
/// nasce solo quando il servizio verifica il codice, e il servizio riverifica account, timbro e tenant sul database.
/// </summary>
public sealed class SfidaSecondoFattoreWeb
{
    public const string NomeCookie = ".DvCms.SecondoFattore";
    public const string PercorsoVerifica = "/admin/accesso/verifica";
    public static readonly TimeSpan Validita = TimeSpan.FromMinutes(5);

    private const string Scopo = "DvCms.SfidaSecondoFattore.v1";

    private readonly IDataProtector _protettore;
    private readonly TimeProvider _orologio;
    private readonly IWebHostEnvironment _ambiente;

    public SfidaSecondoFattoreWeb(IDataProtectionProvider provider, TimeProvider orologio, IWebHostEnvironment ambiente)
    {
        _protettore = provider.CreateProtector(Scopo);
        _orologio = orologio;
        _ambiente = ambiente;
    }

    public void Emetti(HttpContext contesto, SfidaSecondoFattore sfida, Guid idTenantHost, string? destinazione)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(sfida);

        var scadenza = _orologio.GetUtcNow().Add(Validita);
        var carico = JsonSerializer.Serialize(new Carico(sfida.IdUtenza, sfida.TimbroSicurezza, idTenantHost, scadenza.ToUnixTimeSeconds(), destinazione));
        contesto.Response.Cookies.Append(NomeCookie, _protettore.Protect(carico), Opzioni(contesto));
    }

    public bool TentaLeggi(HttpContext contesto, Guid idTenantHost, out ContenutoSfida sfida)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        sfida = new ContenutoSfida(Guid.Empty, Guid.Empty, null);
        if (!contesto.Request.Cookies.TryGetValue(NomeCookie, out var valore) || string.IsNullOrEmpty(valore) || valore.Length > 2048)
        {
            return false;
        }

        try
        {
            var carico = JsonSerializer.Deserialize<Carico>(_protettore.Unprotect(valore));
            if (carico is null
                || carico.Tenant != idTenantHost
                || carico.Scadenza < _orologio.GetUtcNow().ToUnixTimeSeconds())
            {
                return false;
            }

            sfida = new ContenutoSfida(carico.Utenza, carico.Timbro, carico.Destinazione);
            return true;
        }
        catch (Exception eccezione) when (eccezione is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return false;
        }
    }

    public void Rimuovi(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        contesto.Response.Cookies.Delete(NomeCookie, new CookieOptions { Path = ConfigurazioneAccessiWeb.PercorsoAdmin });
    }

    private CookieOptions Opzioni(HttpContext contesto) => new()
    {
        Path = ConfigurazioneAccessiWeb.PercorsoAdmin,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = !_ambiente.IsDevelopment() || contesto.Request.IsHttps,
        IsEssential = true,
        MaxAge = Validita,
    };

    private sealed record Carico(Guid Utenza, Guid Timbro, Guid Tenant, long Scadenza, string? Destinazione);
}

/// <summary>Reindirizzamento dopo l'accesso: solo percorsi locali dell'area admin, mai verso siti esterni.</summary>
public static class DestinazioneAccesso
{
    public static string Sicura(IUrlHelper url, string? destinazione)
    {
        ArgumentNullException.ThrowIfNull(url);
        return !string.IsNullOrEmpty(destinazione)
            && url.IsLocalUrl(destinazione)
            && destinazione.StartsWith(ConfigurazioneAccessiWeb.PercorsoAdmin, StringComparison.OrdinalIgnoreCase)
                ? destinazione
                : ConfigurazioneAccessiWeb.PercorsoAdmin;
    }
}

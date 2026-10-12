namespace DvCms.Web.Accessi;

/// <summary>
/// Cookie temporaneo del consenso OAuth per l'email (ADR-0021): contiene lo stato cifrato (PKCE, <c>state</c>, attore, tenant,
/// scadenza) e vale solo per la pagina di ritorno. HttpOnly, <c>Lax</c> perché il ritorno dal provider è una navigazione fra siti
/// (con <c>Strict</c> il cookie non arriverebbe), 10 minuti.
/// </summary>
public static class CookieCollegamentoEmail
{
    public const string Nome = ".DvCms.CollegamentoEmail";
    public const string Percorso = "/admin/email";

    public static void Imposta(HttpContext contesto, string valore)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        contesto.Response.Cookies.Append(Nome, valore, Opzioni(contesto, TimeSpan.FromMinutes(10)));
    }

    public static string? Leggi(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        return contesto.Request.Cookies.TryGetValue(Nome, out var valore) && !string.IsNullOrWhiteSpace(valore) && valore.Length <= 4000 ? valore : null;
    }

    public static void Elimina(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        contesto.Response.Cookies.Delete(Nome, Opzioni(contesto, null));
    }

    private static CookieOptions Opzioni(HttpContext contesto, TimeSpan? durata)
    {
        var sviluppo = contesto.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        return new CookieOptions
        {
            Path = Percorso,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = !sviluppo || contesto.Request.IsHttps,
            IsEssential = true,
            MaxAge = durata,
        };
    }
}

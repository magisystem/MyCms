namespace DvCms.Web.Accessi;

/// <summary>
/// Cookie temporaneo che lega il browser che ha chiesto il recupero alla sua richiesta: contiene un riferimento casuale
/// (di cui il server conserva solo l'hash). Limitato a <c>/admin/recupero</c>, HttpOnly, Strict, di breve durata.
/// </summary>
public static class CookieRecupero
{
    public const string Nome = ".DvCms.Recupero";
    public const string PercorsoRichiesta = "/admin/recupero";
    public const string PercorsoCodice = "/admin/recupero/codice";

    public static void Imposta(HttpContext contesto, string riferimento)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        contesto.Response.Cookies.Append(Nome, riferimento, Opzioni(contesto, TimeSpan.FromMinutes(30)));
    }

    public static string? Leggi(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        return contesto.Request.Cookies.TryGetValue(Nome, out var valore) && !string.IsNullOrWhiteSpace(valore) && valore.Length <= 100 ? valore : null;
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
            Path = PercorsoRichiesta,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = !sviluppo || contesto.Request.IsHttps,
            IsEssential = true,
            MaxAge = durata,
        };
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Accessi;

/// <summary>
/// Se la 2FA è obbligatoria per il ruolo e l'account non l'ha ancora configurata (ADR-0019), ogni pagina dell'admin lo rimanda
/// alla configurazione, tranne la pagina stessa, l'uscita, l'accesso e il cambio password (che ha la precedenza: la password
/// temporanea si cambia prima di tutto).
/// </summary>
public sealed class FiltroDueFattoriObbligatorio : IAsyncPageFilter
{
    private static readonly string[] _pagineConsentite = ["/Admin/Sicurezza", "/Admin/Password", "/Admin/Esci", "/Admin/Accesso", "/Admin/Verifica"];

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var utente = context.HttpContext.User;
        var percorsoPagina = context.ActionDescriptor.ViewEnginePath;
        var inAreaAdmin = percorsoPagina.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase)
            || context.ActionDescriptor.RelativePath.StartsWith("/Areas/", StringComparison.OrdinalIgnoreCase);

        if (utente.Identity?.IsAuthenticated == true
            && PrincipalSessione.DeveAttivareDueFattori(utente)
            && inAreaAdmin
            && !_pagineConsentite.Contains(percorsoPagina, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new RedirectResult("/admin/sicurezza");
            return;
        }

        await next();
    }
}

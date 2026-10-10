using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Accessi;

/// <summary>
/// Chi ha una password temporanea deve cambiarla al primo accesso: finché non lo fa, ogni pagina dell'admin lo
/// rimanda alla pagina di cambio password (tranne quella stessa, l'uscita e l'accesso).
/// </summary>
public sealed class FiltroCambioPasswordObbligatorio : IAsyncPageFilter
{
    private static readonly string[] _pagineConsentite = ["/Admin/Password", "/Admin/Esci", "/Admin/Accesso", "/Admin/Verifica"];

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
            && PrincipalSessione.DeveCambiarePassword(utente)
            && inAreaAdmin
            && !_pagineConsentite.Contains(percorsoPagina, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new RedirectResult("/admin/password");
            return;
        }

        await next();
    }
}

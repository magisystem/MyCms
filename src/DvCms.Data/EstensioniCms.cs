using DvCms.Models.Cms;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Data;

public static class EstensioniCms
{
    /// <summary>
    /// Salva una pagina nuova con la sua prima revisione. Pagina e revisione si puntano a vicenda e EF Core non sa
    /// ordinare inserimenti circolari: si inserisce prima la pagina senza puntatori (e la revisione), poi si impostano
    /// i puntatori, il tutto in una transazione.
    /// </summary>
    public static async Task SalvaNuovaPaginaAsync(
        this ContestoDatabase contesto,
        Pagina pagina,
        RevisionePagina revisione,
        CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(pagina);
        ArgumentNullException.ThrowIfNull(revisione);

        var ultimaBozza = pagina.IdUltimaRevisioneBozza;
        var pubblicata = pagina.IdRevisionePubblicata;
        pagina.IdUltimaRevisioneBozza = null;
        pagina.IdRevisionePubblicata = null;

        var transazionePropria = contesto.Database.CurrentTransaction is null;
        var transazione = transazionePropria ? await contesto.Database.BeginTransactionAsync(annullamento) : null;
        try
        {
            contesto.Pagine.Add(pagina);
            contesto.RevisioniPagina.Add(revisione);
            await contesto.SaveChangesAsync(annullamento);

            pagina.IdUltimaRevisioneBozza = ultimaBozza;
            pagina.IdRevisionePubblicata = pubblicata;
            await contesto.SaveChangesAsync(annullamento);

            if (transazione is not null)
            {
                await transazione.CommitAsync(annullamento);
            }
        }
        catch
        {
            // Lo stato in memoria non è più affidabile: l'ambito va scartato dal chiamante.
            pagina.IdUltimaRevisioneBozza = ultimaBozza;
            pagina.IdRevisionePubblicata = pubblicata;
            throw;
        }
        finally
        {
            if (transazione is not null)
            {
                await transazione.DisposeAsync();
            }
        }
    }
}

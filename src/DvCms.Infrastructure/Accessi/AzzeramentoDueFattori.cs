using DvCms.Data;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Accessi;

/// <summary>
/// Azzeramento della 2FA di un account (reimpostazione da parte del <c>PlatformAdmin</c> o comando operativo, ADR-0019):
/// segreti e codici di recupero cancellati, timbro cambiato (sessioni chiuse). Modifica l'entità tracciata e i codici nel
/// contesto: il salvataggio, insieme all'audit, spetta al chiamante.
/// </summary>
internal static class AzzeramentoDueFattori
{
    public static async Task ApplicaAsync(ContestoDatabase contesto, Utenza utenza, DateTime adesso, CancellationToken annullamento)
    {
        var idUtenza = utenza.Id;
        var codici = await contesto.CodiciRecuperoUtenza.Where(codice => codice.IdUtenza == idUtenza).ToListAsync(annullamento);
        contesto.CodiciRecuperoUtenza.RemoveRange(codici);

        utenza.SegretoTotpProtetto = null;
        utenza.SegretoTotpInAttesaProtetto = null;
        utenza.DueFattoriAttivi = false;
        utenza.DueFattoriAttivatiIlUtc = null;
        utenza.UltimoPassoTotp = 0;
        utenza.TimbroSicurezza = Guid.NewGuid();
        utenza.AggiornatoIlUtc = adesso;
    }
}

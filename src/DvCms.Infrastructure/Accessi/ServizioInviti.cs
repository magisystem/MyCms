using System.Security.Cryptography;
using System.Text;
using DvCms.Application.Accessi;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Accessi;

/// <summary>
/// Accettazione degli inviti (vedi <see cref="IServizioInviti"/>). Come per il recupero password, legge <c>Utenze</c> senza
/// il filtro globale ma sempre con il tenant dell'host nella query: un link emesso per un tenant non vale su un altro host.
/// </summary>
public sealed class ServizioInviti : IServizioInviti
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IHashPassword _hash;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioInviti(ContestoDatabase contesto, IContestoTenant tenant, IHashPassword hash, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _hash = hash;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<bool> LinkValidoAsync(string? token, CancellationToken annullamento)
    {
        var (invito, utenza) = await TrovaAsync(token, annullamento);
        return invito is not null && utenza is not null;
    }

    public async Task<EsitoReimpostazione> AccettaAsync(string? token, string? nuovaPassword, CancellationToken annullamento)
    {
        var (invito, utenza) = await TrovaAsync(token, annullamento);
        if (invito is null || utenza is null)
        {
            return EsitoReimpostazione.LinkNonValido;
        }

        if (PoliticaPassword.Valida(nuovaPassword) is not null)
        {
            // Il link resta valido: l'invitato può riprovare con una password accettabile.
            return EsitoReimpostazione.PasswordNonValida;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        utenza.HashPassword = _hash.Calcola(nuovaPassword!);
        utenza.PasswordAggiornataIlUtc = adesso;
        utenza.DeveCambiarePassword = false;
        utenza.TimbroSicurezza = Guid.NewGuid();
        utenza.ContatoreAccessiFalliti = 0;
        utenza.BloccatoFinoAlUtc = null;
        utenza.AggiornatoIlUtc = adesso;

        invito.Stato = StatoInvito.Accettato;
        invito.AccettatoIlUtc = adesso;
        var altri = await _contesto.InvitiAccount.IgnoreQueryFilters()
            .Where(candidato => candidato.IdUtenza == utenza.Id && candidato.Id != invito.Id && candidato.Stato == StatoInvito.Inviato)
            .ToListAsync(annullamento);
        foreach (var altro in altri)
        {
            altro.Stato = StatoInvito.Annullato;
        }

        try
        {
            await _audit.RegistraAsync(
                AzioniAudit.InvitoAccettato,
                $"Utenza:{utenza.Id}",
                RegistroAudit.EsitoRiuscito,
                utenza.IdTenant,
                utenza.Id,
                dettagli: null,
                annullamento);
        }
        catch (DbUpdateException)
        {
            // Lo stesso link usato due volte in parallelo (conflitto di concorrenza sull'account): il secondo non cambia nulla.
            return EsitoReimpostazione.LinkNonValido;
        }

        return EsitoReimpostazione.Riuscita;
    }

    private async Task<(InvitoAccount? Invito, Utenza? Utenza)> TrovaAsync(string? token, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var idTenant = _tenant.IdTenant;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token is { Length: > 0 and <= 100 } ? token : "-"));

        var invito = await _contesto.InvitiAccount.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidato => candidato.HashToken == hash && candidato.IdTenant == idTenant, annullamento);
        if (invito is null || invito.Stato != StatoInvito.Inviato || invito.ScadeIlUtc <= adesso)
        {
            return (null, null);
        }

        // L'invito vale finché l'invitato non ha già scelto una password sua (altrimenti la via è il recupero password).
        var utenza = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidata => candidata.Id == invito.IdUtenza
                && candidata.IdTenant == idTenant
                && candidata.Stato == StatoUtenza.Attiva
                && candidata.Ruolo != RuoloUtenza.PlatformAdmin
                && candidata.DeveCambiarePassword,
            annullamento);
        return utenza is null ? (null, null) : (invito, utenza);
    }
}

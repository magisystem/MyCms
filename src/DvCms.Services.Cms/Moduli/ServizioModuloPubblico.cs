using DvCms.Contracts.Moduli;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Moduli;
using DvCms.Data;
using DvCms.Data.Cms;
using DvCms.DTO.Moduli;
using DvCms.Extender;
using DvCms.Models.Moduli;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Cms.Moduli;

/// <summary>Moduli di contatto per il sito pubblico (vedi <see cref="IServizioModuloPubblico"/>).</summary>
public sealed class ServizioModuloPubblico : IServizioModuloPubblico
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly OpzioniModuli _opzioni;
    private readonly NotificheModulo _notifiche;
    private readonly TimeProvider _orologio;

    public ServizioModuloPubblico(ContestoDatabase contesto, IContestoTenant tenant, OpzioniModuli opzioni, TimeProvider orologio, NotificheModulo notifiche)
    {
        _notifiche = notifiche;
        _contesto = contesto;
        _tenant = tenant;
        _opzioni = opzioni;
        _orologio = orologio;
    }

    public async Task<ModuloPubblico?> LeggiAsync(Guid idModulo, string? lingua, CancellationToken annullamento)
    {
        var (modulo, pubblico) = await TrovaAsync(idModulo, lingua, annullamento);
        return modulo is null ? null : pubblico;
    }

    public async Task<RisultatoInvioModulo> InviaAsync(Guid idModulo, string? lingua, IReadOnlyDictionary<string, string[]> valori, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(valori);

        var (modulo, pubblico) = await TrovaAsync(idModulo, lingua, annullamento);
        if (modulo is null || pubblico is null)
        {
            return RisultatoInvioModulo.Semplice(EsitoInvioPubblico.NonTrovato);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var finestra = adesso - TimeSpan.FromHours(24);
        if (await _contesto.InviiModulo.CountAsync(i => i.IdModulo == idModulo && i.CreatoIlUtc >= finestra, annullamento) >= _opzioni.InviiMassimiAlGiorno)
        {
            return RisultatoInvioModulo.Semplice(EsitoInvioPubblico.LimiteRaggiunto) with { Modulo = pubblico };
        }

        var esito = ElaboratoreInvioModulo.Elabora(pubblico.Definizione, pubblico.Lingua, pubblico.LinguaPredefinita, valori);
        if (!esito.Valido)
        {
            return new RisultatoInvioModulo(EsitoInvioPubblico.DatiNonValidi, pubblico, esito.Errori, esito.ValoriRicevuti);
        }

        var invio = new InvioModulo
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = _tenant.IdTenant,
            IdSito = _tenant.IdSito,
            IdModulo = idModulo,
            CodiceLingua = pubblico.Lingua,
            Stato = StatoInvioModulo.Nuovo,
            ContenutoJson = SerializzatoreModulo.ScriviContenuto(esito.Contenuto!),
            CreatoIlUtc = adesso,
        };
        _contesto.InviiModulo.Add(invio);

        // Le email partono dall'outbox, nella stessa transazione della richiesta: se l'accodamento fallisce la richiesta non si perde per metà.
        await _notifiche.AccodaAsync(modulo, SerializzatoreModulo.LeggiImpostazioni(modulo.ImpostazioniJson), invio, pubblico, esito.Contenuto!, annullamento);
        await _contesto.SaveChangesAsync(annullamento);
        return new RisultatoInvioModulo(EsitoInvioPubblico.Accettato, pubblico, esito.Errori, esito.ValoriRicevuti);
    }

    private async Task<(Modulo? Modulo, ModuloPubblico? Pubblico)> TrovaAsync(Guid idModulo, string? lingua, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var modulo = await _contesto.Moduli.AsNoTracking().SingleOrDefaultAsync(m => m.Id == idModulo && m.IdSito == idSito && m.Stato == StatoModulo.Attivo, annullamento);
        if (modulo is null)
        {
            return (null, null);
        }

        var lingue = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var predefinita = lingue.FirstOrDefault(l => l.Predefinita)?.Codice ?? "it";
        var scelta = lingue.FirstOrDefault(l => string.Equals(l.Codice, lingua, StringComparison.OrdinalIgnoreCase))?.Codice ?? predefinita;
        return (modulo, new ModuloPubblico(modulo.Id, modulo.Nome, SerializzatoreModulo.LeggiDefinizione(modulo.DefinizioneJson, predefinita), predefinita, scelta));
    }
}

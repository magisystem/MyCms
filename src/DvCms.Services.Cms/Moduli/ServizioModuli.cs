using DvCms.Contracts.Accessi;
using DvCms.Contracts.Moduli;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Moduli;
using DvCms.Data;
using DvCms.Data.Cms;
using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Moduli;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Cms.Moduli;

/// <summary>Gestione dei moduli di contatto (vedi <see cref="IServizioModuli"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed class ServizioModuli : IServizioModuli
{
    private const string Risorsa = "Moduli";

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioModuli(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<LinguaModulo>> LingueAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediTenantAdminAsync(idAttore, annullamento);
        return await LingueDelSitoAsync(annullamento);
    }

    public async Task<IReadOnlyList<RiepilogoModulo>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediTenantAdminAsync(idAttore, annullamento);

        var idSito = _tenant.IdSito;
        var predefinita = (await LingueDelSitoAsync(annullamento)).First(l => l.Predefinita).Codice;
        var moduli = await _contesto.Moduli.AsNoTracking().Where(m => m.IdSito == idSito).OrderBy(m => m.Nome).ToListAsync(annullamento);
        var conteggi = await _contesto.InviiModulo.AsNoTracking()
            .Where(i => i.IdSito == idSito)
            .GroupBy(i => i.IdModulo)
            .Select(g => new { Id = g.Key, Totali = g.Count(), Nuovi = g.Count(i => i.Stato == StatoInvioModulo.Nuovo) })
            .ToDictionaryAsync(x => x.Id, annullamento);
        return [.. moduli.Select(m => new RiepilogoModulo(
            m.Id,
            m.Nome,
            m.Stato,
            SerializzatoreModulo.LeggiDefinizione(m.DefinizioneJson, predefinita).Campi.Count,
            conteggi.TryGetValue(m.Id, out var c) ? c.Nuovi : 0,
            conteggi.TryGetValue(m.Id, out c) ? c.Totali : 0,
            m.AggiornatoIlUtc))];
    }

    public async Task<VistaModulo?> LeggiAsync(Guid idAttore, Guid idModulo, CancellationToken annullamento)
    {
        await RichiediTenantAdminAsync(idAttore, annullamento);

        var idSito = _tenant.IdSito;
        var modulo = await _contesto.Moduli.AsNoTracking().SingleOrDefaultAsync(m => m.Id == idModulo && m.IdSito == idSito, annullamento);
        if (modulo is null)
        {
            return null;
        }

        var predefinita = (await LingueDelSitoAsync(annullamento)).First(l => l.Predefinita).Codice;
        var totali = await _contesto.InviiModulo.AsNoTracking().CountAsync(i => i.IdModulo == idModulo, annullamento);
        var nuovi = await _contesto.InviiModulo.AsNoTracking().CountAsync(i => i.IdModulo == idModulo && i.Stato == StatoInvioModulo.Nuovo, annullamento);
        return new VistaModulo(
            modulo.Id,
            modulo.Nome,
            modulo.Stato,
            SerializzatoreModulo.LeggiDefinizione(modulo.DefinizioneJson, predefinita),
            SerializzatoreModulo.LeggiImpostazioni(modulo.ImpostazioniJson) with { GiorniConservazione = modulo.GiorniConservazione },
            Convert.ToBase64String(modulo.TokenConcorrenza),
            nuovi,
            totali,
            modulo.AggiornatoIlUtc);
    }

    public async Task<EsitoModulo> CreaAsync(Guid idAttore, DatiModulo dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.CreazioneModulo);
            return Esito(CodiceEsito.NonAutorizzato);
        }

        var errori = ValidaDati(dati, await LingueDelSitoAsync(annullamento));
        if (errori.Count > 0)
        {
            return Esito(CodiceEsito.DatiNonValidi, errori);
        }

        var idSito = _tenant.IdSito;
        if (await _contesto.Moduli.CountAsync(m => m.IdSito == idSito, annullamento) >= Modulo.NumeroMassimoPerSito)
        {
            return Esito(CodiceEsito.NonConsentito, [$"Un sito può avere al massimo {Modulo.NumeroMassimoPerSito} moduli."]);
        }

        var nome = dati.Nome.Trim();
        if (await _contesto.Moduli.AnyAsync(m => m.IdSito == idSito && m.Nome == nome, annullamento))
        {
            return Esito(CodiceEsito.GiaPresente, ["Esiste già un modulo con questo nome."]);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var modulo = new Modulo { Id = Identificativi.Nuovo(_orologio), IdTenant = _tenant.IdTenant, IdSito = idSito, CreatoIlUtc = adesso };
        Applica(modulo, dati, idAttore, adesso);
        _contesto.Moduli.Add(modulo);
        return await SalvaConAuditAsync(modulo, AzioniAudit.CreazioneModulo, idAttore, $"{modulo.Nome}; stato {modulo.Stato}", annullamento);
    }

    public async Task<EsitoModulo> AggiornaAsync(Guid idAttore, Guid idModulo, DatiModulo dati, string? tokenConcorrenza, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.ModificaModulo);
            return Esito(CodiceEsito.NonAutorizzato);
        }

        var errori = ValidaDati(dati, await LingueDelSitoAsync(annullamento));
        if (errori.Count > 0)
        {
            return Esito(CodiceEsito.DatiNonValidi, errori);
        }

        var idSito = _tenant.IdSito;
        var modulo = await _contesto.Moduli.SingleOrDefaultAsync(m => m.Id == idModulo && m.IdSito == idSito, annullamento);
        if (modulo is null)
        {
            return Esito(CodiceEsito.NonTrovato);
        }

        var nome = dati.Nome.Trim();
        if (await _contesto.Moduli.AnyAsync(m => m.IdSito == idSito && m.Nome == nome && m.Id != idModulo, annullamento))
        {
            return Esito(CodiceEsito.GiaPresente, ["Esiste già un modulo con questo nome."]);
        }

        if (!ImpostaToken(modulo, tokenConcorrenza))
        {
            return Esito(CodiceEsito.DatiNonValidi);
        }

        Applica(modulo, dati, idAttore, _orologio.GetUtcNow().UtcDateTime);
        return await SalvaConAuditAsync(modulo, AzioniAudit.ModificaModulo, idAttore, $"{modulo.Nome}; stato {modulo.Stato}", annullamento);
    }

    public async Task<EsitoModulo> ImpostaStatoAsync(Guid idAttore, Guid idModulo, StatoModulo stato, string? tokenConcorrenza, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.CambioStatoModulo);
            return Esito(CodiceEsito.NonAutorizzato);
        }

        if (!Enum.IsDefined(stato))
        {
            return Esito(CodiceEsito.DatiNonValidi);
        }

        var idSito = _tenant.IdSito;
        var modulo = await _contesto.Moduli.SingleOrDefaultAsync(m => m.Id == idModulo && m.IdSito == idSito, annullamento);
        if (modulo is null)
        {
            return Esito(CodiceEsito.NonTrovato);
        }

        var lingue = await LingueDelSitoAsync(annullamento);
        var predefinita = lingue.First(l => l.Predefinita).Codice;
        var dati = new DatiModulo(
            modulo.Nome,
            stato,
            SerializzatoreModulo.LeggiDefinizione(modulo.DefinizioneJson, predefinita),
            SerializzatoreModulo.LeggiImpostazioni(modulo.ImpostazioniJson) with { GiorniConservazione = modulo.GiorniConservazione });
        var errori = ValidaDati(dati, lingue);
        if (errori.Count > 0)
        {
            return Esito(CodiceEsito.DatiNonValidi, errori);
        }

        if (!ImpostaToken(modulo, tokenConcorrenza))
        {
            return Esito(CodiceEsito.DatiNonValidi);
        }

        var precedente = modulo.Stato;
        modulo.Stato = stato;
        modulo.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        modulo.AggiornatoDaIdUtenza = idAttore;
        return await SalvaConAuditAsync(modulo, AzioniAudit.CambioStatoModulo, idAttore, $"{modulo.Nome}; {precedente} -> {stato}", annullamento);
    }

    public async Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idModulo, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.EliminazioneModulo);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        var modulo = await _contesto.Moduli.SingleOrDefaultAsync(m => m.Id == idModulo && m.IdSito == idSito, annullamento);
        if (modulo is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (await _contesto.InviiModulo.AnyAsync(i => i.IdModulo == idModulo, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        _contesto.Moduli.Remove(modulo);
        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.EliminazioneModulo, $"{Risorsa}:{idModulo}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, idAttore, modulo.Nome, annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idModulo);
    }

    private static List<string> ValidaDati(DatiModulo dati, IReadOnlyList<LinguaModulo> lingue) => [.. ValidatoreModulo.Valida(dati, lingue)];

    private static EsitoModulo Esito(CodiceEsito codice, IReadOnlyList<string>? errori = null) => new(codice, null, errori ?? [], null);

    private static void Applica(Modulo modulo, DatiModulo dati, Guid idAttore, DateTime adesso)
    {
        modulo.Nome = dati.Nome.Trim();
        modulo.Stato = dati.Stato;
        modulo.DefinizioneJson = SerializzatoreModulo.Scrivi(Normalizza(dati.Definizione));
        modulo.ImpostazioniJson = SerializzatoreModulo.Scrivi(Normalizza(dati.Impostazioni));
        modulo.GiorniConservazione = dati.Impostazioni.GiorniConservazione;
        modulo.AggiornatoIlUtc = adesso;
        modulo.AggiornatoDaIdUtenza = idAttore;
    }

    /// <summary>Toglie gli spazi e scarta i testi vuoti: nel JSON ci sono solo testi veri.</summary>
    private static DefinizioneModulo Normalizza(DefinizioneModulo d) =>
        new(
            [.. d.Campi.Select(c => c with
            {
                Etichette = Testi(c.Etichette),
                Aiuti = Testi(c.Aiuti),
                Opzioni = [.. c.Opzioni.Select(Testi)],
            })],
            Testi(d.Pulsante),
            Testi(d.Conferma),
            string.IsNullOrWhiteSpace(d.PercorsoDopoInvio) ? null : d.PercorsoDopoInvio.Trim(),
            string.IsNullOrWhiteSpace(d.UrlInformativa) ? null : d.UrlInformativa.Trim());

    private static ImpostazioniModulo Normalizza(ImpostazioniModulo i) =>
        i with { Destinatari = [.. i.Destinatari.Select(a => NormalizzatoreEmail.TentaNormalizza(a, out var n) ? n : a.Trim())] };

    private static Dictionary<string, string> Testi(IReadOnlyDictionary<string, string> testi) =>
        testi.Where(t => !string.IsNullOrWhiteSpace(t.Value)).ToDictionary(t => t.Key, t => t.Value.Trim(), StringComparer.Ordinal);

    private bool ImpostaToken(Modulo modulo, string? tokenConcorrenza)
    {
        if (string.IsNullOrEmpty(tokenConcorrenza))
        {
            return true;
        }

        try
        {
            _contesto.Entry(modulo).Property(m => m.TokenConcorrenza).OriginalValue = Convert.FromBase64String(tokenConcorrenza);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<EsitoModulo> SalvaConAuditAsync(Modulo modulo, string azione, Guid idAttore, string dettagli, CancellationToken annullamento)
    {
        try
        {
            await _audit.RegistraAsync(azione, $"{Risorsa}:{modulo.Id}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, idAttore, dettagli, annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Esito(CodiceEsito.Conflitto);
        }
        catch (DbUpdateException)
        {
            // Un doppio invio con lo stesso nome batte sull'indice univoco: stesso esito di un nome già presente.
            return Esito(CodiceEsito.GiaPresente, ["Esiste già un modulo con questo nome."]);
        }

        return new EsitoModulo(CodiceEsito.Riuscito, modulo.Id, [], Convert.ToBase64String(modulo.TokenConcorrenza));
    }

    private async Task<IReadOnlyList<LinguaModulo>> LingueDelSitoAsync(CancellationToken annullamento) =>
        [.. (await LingueDelSito.ElencaAsync(_contesto, _tenant.IdSito, annullamento)).Select(l => new LinguaModulo(l.Codice, l.Nome, l.Predefinita))];

    private async Task RichiediTenantAdminAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            throw new UnauthorizedAccessException("Operazione riservata al TenantAdmin del tenant.");
        }
    }

    private Task<bool> ETenantAdminAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && candidata.Ruolo == RuoloUtenza.TenantAdmin
                && candidata.IdTenant == idTenant,
            annullamento);
    }

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(c => c.Id == idAttore);
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, Risorsa, RegistroAudit.EsitoNegato, _tenant.IdTenant, attoreEsiste ? idAttore : null, $"azione richiesta: {azione}", CancellationToken.None);
    }
}

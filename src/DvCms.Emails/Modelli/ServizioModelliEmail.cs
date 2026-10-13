using DvCms.Contracts.Accessi;
using DvCms.Contracts.Email;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Email;
using DvCms.Models.Localizzazione;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Emails;

/// <summary>Gestione dei modelli email del tenant da parte del suo <c>TenantAdmin</c> (ADR-0025).</summary>
public sealed class ServizioModelliEmail : IServizioModelliEmail
{
    private const string RisorsaAudit = "ModelliEmail";

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioModelliEmail(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<VoceModelloEmail>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediTenantAdminAsync(idAttore, annullamento);

        var idTenant = _tenant.IdTenant;
        var personalizzati = await _contesto.ModelliEmail.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IdTenant == idTenant)
            .Select(m => new { m.Codice, m.Lingua })
            .ToListAsync(annullamento);
        return [.. CatalogoModelliEmail.Definizioni.Select(definizione => new VoceModelloEmail(
            definizione,
            [.. personalizzati.Where(p => p.Codice == definizione.Codice).Select(p => p.Lingua).Order(StringComparer.Ordinal)]))];
    }

    public async Task<SchedaModelloEmail?> LeggiAsync(Guid idAttore, string codice, string lingua, CancellationToken annullamento)
    {
        await RichiediTenantAdminAsync(idAttore, annullamento);

        var definizione = CatalogoModelliEmail.Trova(codice);
        var disponibili = await LingueDisponibiliAsync(annullamento);
        var richiesta = ComponitoreEmail.NormalizzaLingua(lingua);
        if (definizione is null || !disponibili.Contains(richiesta, StringComparer.Ordinal))
        {
            return null;
        }

        var idTenant = _tenant.IdTenant;
        var predefinita = disponibili[0];
        var righe = await _contesto.ModelliEmail.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IdTenant == idTenant && m.Codice == codice && (m.Lingua == richiesta || m.Lingua == predefinita))
            .ToListAsync(annullamento);

        var propria = righe.FirstOrDefault(m => m.Lingua == richiesta);
        if (propria is not null)
        {
            return Scheda(definizione, richiesta, propria, OrigineModelloEmail.TenantLingua, personalizzato: true, disponibili);
        }

        var dellaPredefinita = righe.FirstOrDefault(m => m.Lingua == predefinita);
        if (dellaPredefinita is not null)
        {
            return Scheda(definizione, richiesta, dellaPredefinita, OrigineModelloEmail.TenantLinguaPredefinita, personalizzato: false, disponibili);
        }

        var fabbrica = CatalogoModelliEmail.Fabbrica(codice, richiesta);
        return new SchedaModelloEmail(
            definizione,
            richiesta,
            fabbrica ?? CatalogoModelliEmail.Fabbrica(codice, Lingua.CodiceItaliano)!,
            fabbrica is not null ? OrigineModelloEmail.FabbricaLingua : OrigineModelloEmail.FabbricaItaliano,
            Personalizzato: false,
            TokenConcorrenza: null,
            disponibili);
    }

    public async Task<EsitoSalvataggioModelloEmail> SalvaAsync(
        Guid idAttore,
        string codice,
        string lingua,
        ContenutoModelloEmail contenuto,
        string? tokenConcorrenza,
        CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(contenuto);

        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.ModificaModelloEmail);
            return new EsitoSalvataggioModelloEmail(CodiceEsito.NonAutorizzato, new EsitoValidazioneModelloEmail([]));
        }

        var definizione = CatalogoModelliEmail.Trova(codice);
        var richiesta = ComponitoreEmail.NormalizzaLingua(lingua);
        if (definizione is null || !(await LingueDisponibiliAsync(annullamento)).Contains(richiesta, StringComparer.Ordinal))
        {
            return new EsitoSalvataggioModelloEmail(CodiceEsito.NonTrovato, new EsitoValidazioneModelloEmail([]));
        }

        var anteprima = await ValidatoreModelloEmail.RendiAsync(definizione, contenuto);
        if (!anteprima.Validazione.Valido)
        {
            return new EsitoSalvataggioModelloEmail(CodiceEsito.DatiNonValidi, anteprima.Validazione);
        }

        var idTenant = _tenant.IdTenant;
        var riga = await _contesto.ModelliEmail.IgnoreQueryFilters().SingleOrDefaultAsync(m => m.IdTenant == idTenant && m.Codice == codice && m.Lingua == richiesta, annullamento);
        if (riga is null)
        {
            riga = new ModelloEmail { Id = Identificativi.Nuovo(_orologio), IdTenant = idTenant, Codice = codice, Lingua = richiesta };
            _contesto.ModelliEmail.Add(riga);
        }
        else if (!string.IsNullOrEmpty(tokenConcorrenza))
        {
            try
            {
                _contesto.Entry(riga).Property(m => m.TokenConcorrenza).OriginalValue = Convert.FromBase64String(tokenConcorrenza);
            }
            catch (FormatException)
            {
                return new EsitoSalvataggioModelloEmail(CodiceEsito.DatiNonValidi, new EsitoValidazioneModelloEmail([]));
            }
        }

        riga.Oggetto = contenuto.Oggetto.Trim();
        riga.CorpoHtml = contenuto.CorpoHtml;
        riga.CorpoTesto = contenuto.CorpoTesto;
        riga.AggiornatoIlUtc = _orologio.GetUtcNow().UtcDateTime;
        riga.AggiornatoDaIdUtenza = idAttore;

        return await SalvaConAuditAsync(AzioniAudit.ModificaModelloEmail, idAttore, $"{codice} ({richiesta})", annullamento)
            ? new EsitoSalvataggioModelloEmail(CodiceEsito.Riuscito, anteprima.Validazione)
            : new EsitoSalvataggioModelloEmail(CodiceEsito.Conflitto, anteprima.Validazione);
    }

    public async Task<EsitoOperazione> RipristinaAsync(Guid idAttore, string codice, string lingua, CancellationToken annullamento)
    {
        if (!await ETenantAdminAsync(idAttore, annullamento))
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.RipristinoModelloEmail);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var richiesta = ComponitoreEmail.NormalizzaLingua(lingua);
        var idTenant = _tenant.IdTenant;
        var riga = await _contesto.ModelliEmail.IgnoreQueryFilters().SingleOrDefaultAsync(m => m.IdTenant == idTenant && m.Codice == codice && m.Lingua == richiesta, annullamento);
        if (riga is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        _contesto.ModelliEmail.Remove(riga);
        return await SalvaConAuditAsync(AzioniAudit.RipristinoModelloEmail, idAttore, $"{codice} ({richiesta})", annullamento)
            ? new EsitoOperazione(CodiceEsito.Riuscito)
            : new EsitoOperazione(CodiceEsito.Conflitto);
    }

    public async Task<AnteprimaModelloEmail?> AnteprimaAsync(Guid idAttore, string codice, ContenutoModelloEmail contenuto, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(contenuto);
        await RichiediTenantAdminAsync(idAttore, annullamento);

        var definizione = CatalogoModelliEmail.Trova(codice);
        return definizione is null ? null : await ValidatoreModelloEmail.RendiAsync(definizione, contenuto);
    }

    private static SchedaModelloEmail Scheda(DefinizioneModelloEmail definizione, string lingua, ModelloEmail riga, OrigineModelloEmail origine, bool personalizzato, IReadOnlyList<string> disponibili) =>
        new(
            definizione,
            lingua,
            new ContenutoModelloEmail(riga.Oggetto, riga.CorpoHtml, riga.CorpoTesto),
            origine,
            personalizzato,
            personalizzato ? Convert.ToBase64String(riga.TokenConcorrenza) : null,
            disponibili);

    /// <summary>Le lingue in cui il tenant può scrivere un modello: quelle abilitate nei suoi siti (la predefinita per prima), almeno l'italiano.</summary>
    private async Task<IReadOnlyList<string>> LingueDisponibiliAsync(CancellationToken annullamento)
    {
        var righe = await (
            from ls in _contesto.LingueSito.AsNoTracking()
            join lingua in _contesto.Lingue.AsNoTracking() on ls.IdLingua equals lingua.Id
            where lingua.Attiva
            select new { lingua.Codice, ls.Predefinita })
            .ToListAsync(annullamento);
        var codici = righe.OrderByDescending(r => r.Predefinita).ThenBy(r => r.Codice, StringComparer.Ordinal).Select(r => r.Codice).Distinct(StringComparer.Ordinal).ToList();
        if (codici.Count == 0)
        {
            codici.Add(Lingua.CodiceItaliano);
        }

        return codici;
    }

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
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, RisorsaAudit, RegistroAudit.EsitoNegato, _tenant.IdTenant, attoreEsiste ? idAttore : null, $"azione richiesta: {azione}", CancellationToken.None);
    }

    private async Task<bool> SalvaConAuditAsync(string azione, Guid idAttore, string dettagli, CancellationToken annullamento)
    {
        try
        {
            await _audit.RegistraAsync(azione, RisorsaAudit, RegistroAudit.EsitoRiuscito, _tenant.IdTenant, idAttore, dettagli, annullamento);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}

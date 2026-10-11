using DvCms.Application.Accessi;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Metodi di spedizione del sito (vedi <see cref="IServizioSpedizioni"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed class ServizioSpedizioni : IServizioSpedizioni
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioSpedizioni(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<MetodoSpedizioneVista>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        return await LeggiAsync(soloAttivi: false, annullamento);
    }

    public async Task<IReadOnlyList<MetodoSpedizioneVista>> ElencaAttiviAsync(Guid idAccount, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli account autenticati del tenant.");
        }

        return await CommercioAttivoAsync(annullamento) ? await LeggiAsync(soloAttivi: true, annullamento) : [];
    }

    public async Task<EsitoOperazione> SalvaAsync(Guid idAttore, DatiMetodoSpedizione dati, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(dati);

        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "SalvataggioMetodoSpedizione");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!await CommercioAttivoAsync(annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var errori = Valida(dati);
        if (errori.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
        }

        var idSito = _tenant.IdSito;
        var esistenti = await _contesto.MetodiSpedizione.Where(m => m.IdSito == idSito).ToListAsync(annullamento);
        MetodoSpedizione? metodo = null;
        if (dati.Id is not null)
        {
            metodo = esistenti.SingleOrDefault(m => m.Id == dati.Id);
            if (metodo is null)
            {
                return new EsitoOperazione(CodiceEsito.NonTrovato);
            }
        }
        else if (esistenti.Count >= MetodoSpedizione.NumeroMassimo)
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var nuovo = metodo is null;
        if (metodo is null)
        {
            metodo = new MetodoSpedizione
            {
                Id = Identificativi.Nuovo(_orologio),
                IdTenant = _tenant.IdTenant,
                IdSito = idSito,
                Posizione = esistenti.Count == 0 ? 0 : esistenti.Max(m => m.Posizione) + 1,
            };
            _contesto.MetodiSpedizione.Add(metodo);
        }

        metodo.Tipo = dati.Tipo;
        metodo.Area = dati.Tipo == TipoMetodoSpedizione.Zona ? dati.Area : null;
        metodo.Nome = dati.Nome!.Trim();
        metodo.TariffaLorda = dati.Tipo == TipoMetodoSpedizione.Zona ? dati.TariffaLorda : 0m;
        metodo.Attivo = dati.Attivo;

        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.ModificaMetodoSpedizione,
            $"MetodoSpedizione:{metodo.Id}",
            RegistroAudit.EsitoRiuscito,
            _tenant.IdTenant,
            attore.Id,
            $"{(nuovo ? "creato" : "modificato")}: {metodo.Tipo}, attivo {metodo.Attivo}",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, metodo.Id);
    }

    public async Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idMetodo, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "EliminazioneMetodoSpedizione");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        var metodo = await _contesto.MetodiSpedizione.SingleOrDefaultAsync(m => m.Id == idMetodo && m.IdSito == idSito, annullamento);
        if (metodo is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        _contesto.MetodiSpedizione.Remove(metodo);
        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(
            AzioniAudit.ModificaMetodoSpedizione,
            $"MetodoSpedizione:{idMetodo}",
            RegistroAudit.EsitoRiuscito,
            _tenant.IdTenant,
            attore.Id,
            "eliminato",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito);
    }

    private static List<string> Valida(DatiMetodoSpedizione dati)
    {
        var errori = new List<string>();
        var nome = dati.Nome?.Trim() ?? string.Empty;
        if (nome.Length is 0 or > MetodoSpedizione.LunghezzaMassimaNome || nome.Any(char.IsControl))
        {
            errori.Add("Nome");
        }

        switch (dati.Tipo)
        {
            case TipoMetodoSpedizione.Zona:
                if (dati.Area is not (AreaSpedizione.Italia or AreaSpedizione.UnioneEuropea))
                {
                    errori.Add("Area");
                }

                if (!MetodoSpedizione.TariffaValida(dati.TariffaLorda))
                {
                    errori.Add("TariffaLorda");
                }

                break;
            case TipoMetodoSpedizione.Ritiro:
                if (dati.Area is not null)
                {
                    errori.Add("Area");
                }

                break;
            default:
                errori.Add("Tipo");
                break;
        }

        return errori;
    }

    private async Task<IReadOnlyList<MetodoSpedizioneVista>> LeggiAsync(bool soloAttivi, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var query = _contesto.MetodiSpedizione.AsNoTracking().Where(m => m.IdSito == idSito);
        if (soloAttivi)
        {
            query = query.Where(m => m.Attivo);
        }

        var metodi = await query.OrderBy(m => m.Posizione).ThenBy(m => m.Nome).ToListAsync(annullamento);
        return [.. metodi.Select(m => new MetodoSpedizioneVista(m.Id, m.Tipo, m.Area, m.Nome, m.TariffaLorda, m.Attivo))];
    }

    private Task<bool> CommercioAttivoAsync(CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return _contesto.Siti.AsNoTracking().AnyAsync(sito => sito.Id == idSito && sito.CommercioAttivo, annullamento);
    }

    private Task<Utenza?> TrovaAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
    }

    private Task<Utenza?> TrovaAccountAsync(Guid idAccount, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAccount
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.IdTenant == idTenant && (candidata.Ruolo == RuoloUtenza.Utente || candidata.Ruolo == RuoloUtenza.TenantAdmin))),
            annullamento);
    }

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "MetodoSpedizione",
            RegistroAudit.EsitoNegato,
            _tenant.IdTenant,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {azione}",
            CancellationToken.None);
    }
}

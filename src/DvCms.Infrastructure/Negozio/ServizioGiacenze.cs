using DvCms.Contracts.Accessi;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.Data.Cms;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Infrastructure.Cms;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Giacenza e prenotazioni (vedi <see cref="IServizioGiacenze"/>).</summary>
public sealed class ServizioGiacenze : IServizioGiacenze
{
    private const int LottoScadenze = 100;

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioGiacenze(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<IReadOnlyList<GiacenzaVista>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        await RilasciaScaduteAsync(annullamento);
        var idSito = _tenant.IdSito;
        if (!await _contesto.Siti.AsNoTracking().AnyAsync(s => s.Id == idSito && s.CommercioAttivo, annullamento))
        {
            return [];
        }

        var predefinita = (await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento)).FirstOrDefault(l => l.Predefinita);
        var varianti = await _contesto.VariantiProdotto.AsNoTracking().Where(v => v.IdSito == idSito).OrderBy(v => v.Sku).ToListAsync(annullamento);
        var giacenze = await _contesto.GiacenzeVarianti.AsNoTracking().Where(g => g.IdSito == idSito).ToDictionaryAsync(g => g.IdVariante, annullamento);
        var nomi = predefinita is null
            ? []
            : await _contesto.TraduzioniProdotto.AsNoTracking().Where(t => t.IdSito == idSito && t.IdLingua == predefinita.IdLingua).ToDictionaryAsync(t => t.IdProdotto, t => t.Nome, annullamento);
        var etichette = predefinita is null
            ? []
            : await _contesto.TraduzioniVarianteProdotto.AsNoTracking().Where(t => t.IdSito == idSito && t.IdLingua == predefinita.IdLingua).ToDictionaryAsync(t => t.IdVariante, t => t.Etichetta, annullamento);

        return [.. varianti.Select(v =>
        {
            var g = giacenze.GetValueOrDefault(v.Id);
            return new GiacenzaVista(v.Id, v.Sku, nomi.GetValueOrDefault(v.IdProdotto) ?? string.Empty, etichette.GetValueOrDefault(v.Id), g is not null, g?.Disponibile ?? 0, g?.Riservata ?? 0);
        })];
    }

    public async Task<EsitoOperazione> ImpostaAsync(Guid idAttore, Guid idVariante, int? disponibile, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        if (!await _contesto.Siti.AsNoTracking().AnyAsync(s => s.Id == idSito && s.CommercioAttivo, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        if (disponibile is < 0 or > GiacenzaVariante.QuantitaMassima)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Disponibile"]);
        }

        if (!await _contesto.VariantiProdotto.AsNoTracking().AnyAsync(v => v.Id == idVariante && v.IdSito == idSito, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var giacenza = await _contesto.GiacenzeVarianti.SingleOrDefaultAsync(g => g.IdVariante == idVariante && g.IdSito == idSito, annullamento);
        var esisteva = giacenza is not null;
        var prima = giacenza?.Disponibile ?? 0;

        if (disponibile is null)
        {
            if (giacenza is null)
            {
                return new EsitoOperazione(CodiceEsito.Riuscito);
            }

            if (giacenza.Riservata > 0)
            {
                return new EsitoOperazione(CodiceEsito.NonConsentito, null, ["Riservata"]);
            }

            _contesto.GiacenzeVarianti.Remove(giacenza);
        }
        else
        {
            if (giacenza is null)
            {
                giacenza = new GiacenzaVariante { IdTenant = _tenant.IdTenant, IdSito = idSito, IdVariante = idVariante };
                _contesto.GiacenzeVarianti.Add(giacenza);
            }
            else if (disponibile.Value < giacenza.Riservata)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Disponibile"]);
            }

            giacenza.Disponibile = disponibile.Value;
            giacenza.AggiornataIlUtc = adesso;
        }

        if (!esisteva || (disponibile ?? 0) != prima)
        {
            _contesto.MovimentiGiacenza.Add(new MovimentoGiacenza
            {
                Id = Identificativi.Nuovo(_orologio),
                IdTenant = _tenant.IdTenant,
                IdSito = idSito,
                IdVariante = idVariante,
                Motivo = MotivoMovimento.Impostazione,
                Variazione = (disponibile ?? 0) - prima,
                DisponibileDopo = disponibile ?? 0,
                IdAttore = attore.Id,
                CreatoIlUtc = adesso,
            });
        }

        try
        {
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Una prenotazione è passata fra la lettura e il salvataggio: la cifra mostrata non è più quella vera.
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
        catch (DbUpdateException)
        {
            _contesto.ChangeTracker.Clear();
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        await _audit.RegistraAsync(
            AzioniAudit.ImpostazioneGiacenza,
            $"Variante:{idVariante}",
            RegistroAudit.EsitoRiuscito,
            _tenant.IdTenant,
            attore.Id,
            disponibile is null ? "giacenza non più gestita" : $"disponibile {prima} → {disponibile}",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idVariante);
    }

    public async Task<EsitoOperazione> ReinserisciOrdineAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito, annullamento);
        if (ordine is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (ordine.Stato != StatoOrdine.Rimborsato)
        {
            return new EsitoOperazione(CodiceEsito.NonConsentito);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);

        // Una volta sola: chi arriva per secondo non trova più l'ordine «da reinserire».
        var presa = await _contesto.Ordini.Where(o => o.Id == idOrdine && o.GiacenzaReinseritaIlUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.GiacenzaReinseritaIlUtc, adesso), annullamento);
        if (presa == 0)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        var righe = await _contesto.RigheOrdine.AsNoTracking().Where(r => r.IdOrdine == idOrdine).OrderBy(r => r.IdVariante).ToListAsync(annullamento);
        var reinserite = 0;
        foreach (var riga in righe.GroupBy(r => r.IdVariante).Select(g => (IdVariante: g.Key, Quantita: g.Sum(r => r.Quantita))))
        {
            var q = riga.Quantita;
            var massima = GiacenzaVariante.QuantitaMassima;
            var aggiornate = await _contesto.GiacenzeVarianti
                .Where(g => g.IdVariante == riga.IdVariante && g.IdSito == idSito)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(g => g.Disponibile, g => g.Disponibile + q > massima ? massima : g.Disponibile + q).SetProperty(g => g.AggiornataIlUtc, adesso),
                    annullamento);
            if (aggiornate == 0)
            {
                // Giacenza non gestita (o variante tolta dal catalogo): non c'è nulla da rimettere.
                continue;
            }

            var dopo = await _contesto.GiacenzeVarianti.AsNoTracking().Where(g => g.IdVariante == riga.IdVariante).Select(g => g.Disponibile).SingleAsync(annullamento);
            _contesto.MovimentiGiacenza.Add(new MovimentoGiacenza
            {
                Id = Identificativi.Nuovo(_orologio),
                IdTenant = idTenant,
                IdSito = idSito,
                IdVariante = riga.IdVariante,
                Motivo = MotivoMovimento.Reso,
                Variazione = q,
                DisponibileDopo = dopo,
                IdOrdine = idOrdine,
                IdAttore = attore.Id,
                CreatoIlUtc = adesso,
            });
            reinserite++;
        }

        await _contesto.SaveChangesAsync(annullamento);
        await transazione.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.ReinserimentoGiacenza, $"Ordine:{idOrdine}", RegistroAudit.EsitoRiuscito, idTenant, attore.Id, $"ordine {ordine.Numero}: {reinserite} articoli rimessi in giacenza", annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idOrdine);
    }

    public Task<int> RilasciaScaduteAsync(CancellationToken annullamento) =>
        RiprovaSuDeadlock.EseguiAsync(_contesto, () => RilasciaScaduteUnaVoltaAsync(annullamento));

    private async Task<int> RilasciaScaduteUnaVoltaAsync(CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var scadute = await _contesto.PrenotazioniGiacenza.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Stato == StatoPrenotazione.Attiva && p.ScadeIlUtc <= adesso)
            .Select(p => new { p.IdTenant, p.IdOrdine })
            .Distinct()
            .Take(LottoScadenze)
            .ToListAsync(annullamento);

        var annullati = 0;
        foreach (var scaduta in scadute)
        {
            var proprietaria = _contesto.Database.CurrentTransaction is null;
            var transazione = proprietaria ? await _contesto.Database.BeginTransactionAsync(annullamento) : null;
            try
            {
                // Solo un ordine ancora non pagato scade; uno pagato o già chiuso non si tocca (e non si rilascia nulla).
                var cambiati = await _contesto.Ordini.IgnoreQueryFilters()
                    .Where(o => o.Id == scaduta.IdOrdine && o.IdTenant == scaduta.IdTenant
                        && (o.Stato == StatoOrdine.InAttesaDiPagamento || o.Stato == StatoOrdine.PagamentoFallito))
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Annullato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
                if (cambiati == 0)
                {
                    continue;
                }

                await OperazioniGiacenza.RilasciaAsync(_contesto, scaduta.IdTenant, scaduta.IdOrdine, adesso, annullamento);
                if (transazione is not null)
                {
                    await transazione.CommitAsync(annullamento);
                }
            }
            finally
            {
                if (transazione is not null)
                {
                    await transazione.DisposeAsync();
                }
            }

            annullati++;
            await _audit.RegistraAsync(AzioniAudit.ScadenzaOrdine, $"Ordine:{scaduta.IdOrdine}", RegistroAudit.EsitoRiuscito, scaduta.IdTenant, null, "prenotazione scaduta", annullamento);
        }

        return annullati;
    }

    public async Task<EsitoOperazione> ConfermaOrdineAsync(Guid idOrdine, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == _tenant.IdSito, annullamento);
        if (ordine is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var proprietaria = _contesto.Database.CurrentTransaction is null;
        var transazione = proprietaria ? await _contesto.Database.BeginTransactionAsync(annullamento) : null;
        try
        {
            if (!await OperazioniGiacenza.ConfermaAsync(_contesto, idTenant, ordine.IdSito, idOrdine, adesso, () => Identificativi.Nuovo(_orologio), annullamento))
            {
                return new EsitoOperazione(CodiceEsito.Conflitto);
            }

            await _contesto.SaveChangesAsync(annullamento);
            if (transazione is not null)
            {
                await transazione.CommitAsync(annullamento);
            }
        }
        finally
        {
            if (transazione is not null)
            {
                await transazione.DisposeAsync();
            }
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, idOrdine);
    }

    public async Task<EsitoOperazione> RilasciaOrdineAsync(Guid idOrdine, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        if (!await _contesto.Ordini.AsNoTracking().AnyAsync(o => o.Id == idOrdine && o.IdSito == _tenant.IdSito, annullamento))
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var proprietaria = _contesto.Database.CurrentTransaction is null;
        var transazione = proprietaria ? await _contesto.Database.BeginTransactionAsync(annullamento) : null;
        try
        {
            await OperazioniGiacenza.RilasciaAsync(_contesto, idTenant, idOrdine, adesso, annullamento);
            if (transazione is not null)
            {
                await transazione.CommitAsync(annullamento);
            }
        }
        finally
        {
            if (transazione is not null)
            {
                await transazione.DisposeAsync();
            }
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, idOrdine);
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

    private async Task RegistraNegazioneAsync(Guid idAttore)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "Giacenza", RegistroAudit.EsitoNegato, _tenant.IdTenant, attoreEsiste ? idAttore : null, "azione richiesta: ImpostazioneGiacenza", CancellationToken.None);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Infrastructure.Accessi;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>
/// Dati personali degli acquirenti (A7, ADR-0020). L'account anonimizzato resta come riga vuota — email e nome sostituiti da valori
/// non riconducibili alla persona, credenziali inutilizzabili, nessun accesso possibile — perché gli ordini lo referenziano
/// (vincolo di integrità) e vanno conservati. Gli ordini non pagati si annullano nella stessa transazione, liberando la giacenza.
/// </summary>
public sealed class ServizioDatiPersonali : IServizioDatiPersonali
{
    private const string DominioAnonimo = "anonimizzato.invalid";
    private const string NomeAnonimo = "Account eliminato";
    private const int LottoOrdini = 200;

    private static readonly JsonSerializerOptions _opzioniJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IHashPassword _hash;
    private readonly ContatoreTentativi _contatore;
    private readonly IServizioAudit _audit;
    private readonly OpzioniDatiPersonali _opzioni;
    private readonly TimeProvider _orologio;

    public ServizioDatiPersonali(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IHashPassword hash,
        ContatoreTentativi contatore,
        IServizioAudit audit,
        OpzioniDatiPersonali opzioni,
        TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _hash = hash;
        _contatore = contatore;
        _audit = audit;
        _opzioni = opzioni;
        _orologio = orologio;
    }

    public async Task<EsportazioneDatiPersonali?> EsportaMieiAsync(Guid idAccount, CancellationToken annullamento)
    {
        var account = await TrovaAcquirenteAsync(idAccount, soloAttivo: true, annullamento);
        if (account is null)
        {
            return null;
        }

        var ordini = await _contesto.Ordini.AsNoTracking().Where(o => o.IdUtenza == idAccount).OrderBy(o => o.CreatoIlUtc).ToListAsync(annullamento);
        var idOrdini = ordini.Select(o => o.Id).ToList();
        var righe = await _contesto.RigheOrdine.AsNoTracking().Where(r => idOrdini.Contains(r.IdOrdine)).OrderBy(r => r.Posizione).ToListAsync(annullamento);

        var esportazione = new
        {
            Esportazione = new { CreataIlUtc = Iso(_orologio.GetUtcNow().UtcDateTime), Sito = _tenant.Host },
            Account = new
            {
                account.Email,
                Nome = account.NomeVisualizzato,
                RegistratoIlUtc = Iso(account.CreatoIlUtc),
                UltimoAccessoIlUtc = account.UltimoAccessoIlUtc is { } accesso ? Iso(accesso) : null,
            },
            Ordini = ordini.Select(ordine => new
            {
                ordine.Numero,
                Stato = ordine.Stato.ToString(),
                CreatoIlUtc = Iso(ordine.CreatoIlUtc),
                ordine.Valuta,
                ordine.TotaleLordo,
                ordine.TotaleImponibile,
                ordine.TotaleIva,
                ordine.TotaleRimborsato,
                Spedizione = new
                {
                    Metodo = ordine.NomeSpedizione,
                    Importo = ordine.SpedizioneLorda,
                    ordine.Destinatario,
                    ordine.IndirizzoRiga1,
                    ordine.IndirizzoRiga2,
                    ordine.Cap,
                    ordine.Citta,
                    ordine.Paese,
                },
                Email = ordine.EmailAcquirente,
                Nome = ordine.NomeAcquirente,
                DatiPersonaliAnonimizzatiIlUtc = ordine.AnonimizzatoIlUtc is { } anonimizzato ? Iso(anonimizzato) : null,
                Righe = righe.Where(r => r.IdOrdine == ordine.Id).Select(riga => new
                {
                    riga.Sku,
                    Prodotto = riga.NomeProdotto,
                    Variante = riga.EtichettaVariante,
                    riga.Quantita,
                    riga.PrezzoUnitarioLordo,
                    riga.AliquotaIva,
                    riga.Lordo,
                }),
            }),
        };

        await _audit.RegistraAsync(AzioniAudit.EsportazioneDatiPersonali, $"Utenza:{account.Id}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, account.Id, $"{ordini.Count} ordini", annullamento);
        return new EsportazioneDatiPersonali("dati-personali.json", JsonSerializer.Serialize(esportazione, _opzioniJson));
    }

    public async Task<EsitoOperazione> AnonimizzaMioAccountAsync(Guid idAccount, string? password, CancellationToken annullamento)
    {
        var account = await TrovaAcquirenteAsync(idAccount, soloAttivo: true, annullamento);
        if (account is null)
        {
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        if (account.BloccatoFinoAlUtc is { } blocco && blocco > adesso)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi);
        }

        if (string.IsNullOrEmpty(password) || !_hash.Verifica(password, account.HashPassword))
        {
            await _contatore.RegistraFallimentoAsync(account.Id, adesso, annullamento);
            await _audit.RegistraAsync(AzioniAudit.AnonimizzazioneAccount, $"Utenza:{account.Id}", RegistroAudit.EsitoNegato, _tenant.IdTenant, account.Id, "password non corretta", annullamento);
            return new EsitoOperazione(CodiceEsito.DatiNonValidi);
        }

        return await AnonimizzaAsync(account.Id, account.Id, "su richiesta dell'interessato", annullamento);
    }

    public async Task<EsitoOperazione> AnonimizzaAcquirenteDiOrdineAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var attore = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin
                    || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
        if (attore is null)
        {
            var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore, annullamento);
            await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "DatiPersonali", RegistroAudit.EsitoNegato, idTenant, attoreEsiste ? idAttore : null, "azione richiesta: AnonimizzazioneAccount", CancellationToken.None);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idAcquirente = await _contesto.Ordini.AsNoTracking().Where(o => o.Id == idOrdine).Select(o => (Guid?)o.IdUtenza).SingleOrDefaultAsync(annullamento);
        var acquirente = idAcquirente is { } id ? await TrovaAcquirenteAsync(id, soloAttivo: false, annullamento) : null;
        if (acquirente is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        return await AnonimizzaAsync(acquirente.Id, attore.Id, "su richiesta dell'interessato, da un amministratore", annullamento);
    }

    public async Task<int> AnonimizzaOrdiniScadutiAsync(CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var limite = adesso.AddYears(-_opzioni.ConservazioneOrdiniAnni);
        var totale = 0;
        int trattati;
        do
        {
            annullamento.ThrowIfCancellationRequested();
            var lotto = await _contesto.Ordini.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.AnonimizzatoIlUtc == null && o.AggiornatoIlUtc < limite)
                .OrderBy(o => o.AggiornatoIlUtc)
                .Select(o => new { o.Id, o.IdTenant })
                .Take(LottoOrdini)
                .ToListAsync(annullamento);

            trattati = 0;
            foreach (var gruppo in lotto.GroupBy(o => o.IdTenant))
            {
                var ids = gruppo.Select(o => o.Id).ToList();
                var idTenant = gruppo.Key;
                trattati += await _contesto.Ordini.IgnoreQueryFilters()
                    .Where(o => ids.Contains(o.Id) && o.IdTenant == idTenant && o.AnonimizzatoIlUtc == null && o.AggiornatoIlUtc < limite)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(o => o.AnonimizzatoIlUtc, adesso)
                            .SetProperty(o => o.EmailAcquirente, "anonimo@" + DominioAnonimo)
                            .SetProperty(o => o.NomeAcquirente, NomeAnonimo)
                            .SetProperty(o => o.Destinatario, (string?)null)
                            .SetProperty(o => o.IndirizzoRiga1, (string?)null)
                            .SetProperty(o => o.IndirizzoRiga2, (string?)null)
                            .SetProperty(o => o.Cap, (string?)null)
                            .SetProperty(o => o.Citta, (string?)null),
                        annullamento);
                await _audit.RegistraAsync(AzioniAudit.AnonimizzazioneOrdini, "Ordini", RegistroAudit.EsitoRiuscito, idTenant, null, $"{ids.Count} ordini oltre la conservazione", annullamento);
            }

            totale += trattati;
        }
        while (trattati >= LottoOrdini);

        return totale;
    }

    private Task<Utenza?> TrovaAcquirenteAsync(Guid idAccount, bool soloAttivo, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAccount
                && candidata.IdTenant == idTenant
                && candidata.Ruolo == RuoloUtenza.Utente
                && (candidata.Stato == StatoUtenza.Attiva || (!soloAttivo && candidata.Stato == StatoUtenza.Disabilitata)),
            annullamento);
    }

    private Task<EsitoOperazione> AnonimizzaAsync(Guid idAcquirente, Guid idAttore, string motivo, CancellationToken annullamento) =>
        RiprovaSuDeadlock.EseguiAsync(_contesto, () => AnonimizzaUnaVoltaAsync(idAcquirente, idAttore, motivo, annullamento));

    private async Task<EsitoOperazione> AnonimizzaUnaVoltaAsync(Guid idAcquirente, Guid idAttore, string motivo, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);

        var acquirente = await _contesto.Utenze.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidata => candidata.Id == idAcquirente && candidata.IdTenant == idTenant && candidata.Ruolo == RuoloUtenza.Utente
                && (candidata.Stato == StatoUtenza.Attiva || candidata.Stato == StatoUtenza.Disabilitata),
            annullamento);
        if (acquirente is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var emailNormalizzata = acquirente.EmailNormalizzata;

        // Ordini non ancora pagati: si annullano e la giacenza torna disponibile. Gli altri restano, con la copia dei dati.
        var daAnnullare = await _contesto.Ordini.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.IdTenant == idTenant && o.IdUtenza == idAcquirente
                && (o.Stato == StatoOrdine.InAttesaDiPagamento || o.Stato == StatoOrdine.PagamentoFallito || o.Stato == StatoOrdine.DaSpedireContrassegno))
            .Select(o => o.Id)
            .ToListAsync(annullamento);
        var annullati = 0;
        foreach (var idOrdine in daAnnullare)
        {
            var cambiati = await _contesto.Ordini.IgnoreQueryFilters()
                .Where(o => o.Id == idOrdine && o.IdTenant == idTenant && (o.Stato == StatoOrdine.InAttesaDiPagamento || o.Stato == StatoOrdine.PagamentoFallito || o.Stato == StatoOrdine.DaSpedireContrassegno))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Annullato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
            if (cambiati > 0)
            {
                await OperazioniGiacenza.RilasciaAsync(_contesto, idTenant, idOrdine, adesso, annullamento);
                annullati++;
            }
        }

        var righeCarrello = _contesto.Carrelli.IgnoreQueryFilters().Where(c => c.IdTenant == idTenant && c.IdUtenza == idAcquirente).Select(c => c.Id);
        await _contesto.RigheCarrello.IgnoreQueryFilters().Where(r => righeCarrello.Contains(r.IdCarrello)).ExecuteDeleteAsync(annullamento);
        await _contesto.Carrelli.IgnoreQueryFilters().Where(c => c.IdTenant == idTenant && c.IdUtenza == idAcquirente).ExecuteDeleteAsync(annullamento);
        await _contesto.ConfermeEmailUtenza.IgnoreQueryFilters().Where(c => c.IdUtenza == idAcquirente).ExecuteDeleteAsync(annullamento);
        await _contesto.CodiciRecuperoUtenza.IgnoreQueryFilters().Where(c => c.IdUtenza == idAcquirente).ExecuteDeleteAsync(annullamento);
        await _contesto.RichiesteRecuperoPassword.IgnoreQueryFilters()
            .Where(r => r.IdTenant == idTenant && (r.IdUtenza == idAcquirente || r.EmailNormalizzata == emailNormalizzata))
            .ExecuteDeleteAsync(annullamento);

        var anonimo = "anonimo-" + idAcquirente.ToString("N", CultureInfo.InvariantCulture) + "@" + DominioAnonimo;
        acquirente.Email = anonimo;
        acquirente.EmailNormalizzata = anonimo;
        acquirente.NomeVisualizzato = NomeAnonimo;
        acquirente.HashPassword = _hash.Calcola(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        acquirente.DeveCambiarePassword = true;
        acquirente.Stato = StatoUtenza.Anonimizzata;
        acquirente.TimbroSicurezza = Guid.NewGuid();
        acquirente.SegretoTotpProtetto = null;
        acquirente.SegretoTotpInAttesaProtetto = null;
        acquirente.DueFattoriAttivi = false;
        acquirente.DueFattoriAttivatiIlUtc = null;
        acquirente.ContatoreAccessiFalliti = 0;
        acquirente.BloccatoFinoAlUtc = null;
        acquirente.UltimoAccessoIlUtc = null;
        acquirente.AggiornatoIlUtc = adesso;

        await _contesto.SaveChangesAsync(annullamento);
        await transazione.CommitAsync(annullamento);

        await _audit.RegistraAsync(
            AzioniAudit.AnonimizzazioneAccount,
            $"Utenza:{idAcquirente}",
            RegistroAudit.EsitoRiuscito,
            idTenant,
            idAttore,
            $"{motivo}; ordini annullati: {annullati.ToString(CultureInfo.InvariantCulture)}",
            annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idAcquirente);
    }

    private static string Iso(DateTime istante) => DateTime.SpecifyKind(istante, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}

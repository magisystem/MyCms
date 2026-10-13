using DvCms.Contracts.Accessi;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.Extender;
using DvCms.Infrastructure.Negozio;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Pagamenti;

/// <summary>Rimborsi (vedi <see cref="IServizioRimborsi"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed partial class ServizioRimborsi : IServizioRimborsi
{
    private static readonly StatoOrdine[] _rimborsabili = [StatoOrdine.Pagato, StatoOrdine.Spedito, StatoOrdine.Ritirato];

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IProviderPagamento _provider;
    private readonly IArchivioCredenzialiPagamento _credenziali;
    private readonly Dictionary<string, IGatewayPagamento> _gateway;
    private readonly IServizioAudit _audit;
    private readonly NotificheOrdine _notifiche;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ServizioRimborsi> _logger;

    public ServizioRimborsi(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IProviderPagamento provider,
        IArchivioCredenzialiPagamento credenziali,
        IEnumerable<IGatewayPagamento> gateway,
        IServizioAudit audit,
        NotificheOrdine notifiche,
        TimeProvider orologio,
        ILogger<ServizioRimborsi> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _provider = provider;
        _credenziali = credenziali;
        _gateway = gateway.ToDictionary(g => g.Nome, StringComparer.Ordinal);
        _audit = audit;
        _notifiche = notifiche;
        _orologio = orologio;
        _logger = logger;
    }

    public async Task<RisultatoRimborso> RimborsaOrdineAsync(Guid idAttore, Guid idOrdine, decimal? importo, string? motivo, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "RimborsoOrdine", annullamento);
            return RisultatoRimborso.NonAutorizzato;
        }

        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito, annullamento);
        if (ordine is null)
        {
            return RisultatoRimborso.NonTrovato;
        }

        var tentativo = await _contesto.TentativiPagamento.AsNoTracking()
            .SingleOrDefaultAsync(t => t.IdOrdine == idOrdine && t.Stato == StatoTentativoPagamento.Catturato, annullamento);
        // I rimborsi dal pannello esistono solo per PayPal: un incasso Nexi si restituisce dal back office Nexi (ADR-0022).
        if (!_rimborsabili.Contains(ordine.Stato) || tentativo?.IdCattura is null || tentativo.Provider != NomiProviderPagamento.PayPal)
        {
            return RisultatoRimborso.NonRimborsabile;
        }

        var residuo = ordine.TotaleLordo - ordine.TotaleRimborsato;
        var daRimborsare = importo ?? residuo;
        var testoMotivo = PulisciMotivo(motivo, out var motivoValido);
        if (!motivoValido || daRimborsare <= 0 || daRimborsare > residuo || decimal.Round(daRimborsare, 2) != daRimborsare)
        {
            return RisultatoRimborso.DatiNonValidi;
        }

        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, idSito, annullamento);
        if (credenziali is null)
        {
            return RisultatoRimborso.NonConfigurato;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var rimborso = new Rimborso
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = _tenant.IdTenant,
            IdSito = idSito,
            IdOrdine = idOrdine,
            IdTentativo = tentativo.Id,
            IncassoOrfano = false,
            Importo = daRimborsare,
            Valuta = ordine.Valuta,
            Motivo = testoMotivo,
            Stato = StatoRimborso.InCorso,
            IdUtenzaRichiedente = attore.Id,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };

        // L'importo si impegna sull'ordine prima di chiamare il provider, con un aggiornamento che rispetta il totale: due richieste
        // insieme non possono rimborsare più di quanto resta. Se il provider fallisce l'importo si libera.
        var impegnato = await RiprovaSuDeadlock.EseguiAsync(_contesto, async () =>
        {
            await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
            var importoDaImpegnare = daRimborsare;
            var righe = await _contesto.Ordini
                .Where(o => o.Id == idOrdine && _rimborsabili.Contains(o.Stato) && o.TotaleRimborsato + importoDaImpegnare <= o.TotaleLordo)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TotaleRimborsato, o => o.TotaleRimborsato + importoDaImpegnare).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
            if (righe == 0)
            {
                return false;
            }

            _contesto.Rimborsi.Add(rimborso);
            await _contesto.SaveChangesAsync(annullamento);
            await transazione.CommitAsync(annullamento);
            return true;
        });
        if (!impegnato)
        {
            return RisultatoRimborso.DatiNonValidi;
        }

        await _audit.RegistraAsync(AzioniAudit.RichiestaRimborso, $"Ordine:{idOrdine}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, attore.Id, $"ordine {ordine.Numero}: {daRimborsare:0.00} {ordine.Valuta}", annullamento);
        return await EseguiAsync(rimborso, tentativo.IdCattura, credenziali, annullamento);
    }

    public async Task<RisultatoRimborso> RimborsaIncassoAsync(Guid idAttore, Guid idTentativo, string? motivo, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "RimborsoIncasso", annullamento);
            return RisultatoRimborso.NonAutorizzato;
        }

        var idSito = _tenant.IdSito;
        var tentativo = await _contesto.TentativiPagamento.AsNoTracking().SingleOrDefaultAsync(t => t.Id == idTentativo && t.IdSito == idSito, annullamento);
        if (tentativo is null)
        {
            return RisultatoRimborso.NonTrovato;
        }

        var testoMotivo = PulisciMotivo(motivo, out var motivoValido);
        if (!motivoValido)
        {
            return RisultatoRimborso.DatiNonValidi;
        }

        if (tentativo.Stato != StatoTentativoPagamento.DaRimborsare || tentativo.IdCattura is null || tentativo.Provider != NomiProviderPagamento.PayPal)
        {
            return RisultatoRimborso.NonRimborsabile;
        }

        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, idSito, annullamento);
        if (credenziali is null)
        {
            return RisultatoRimborso.NonConfigurato;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var rimborso = new Rimborso
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = _tenant.IdTenant,
            IdSito = idSito,
            IdOrdine = tentativo.IdOrdine,
            IdTentativo = tentativo.Id,
            IncassoOrfano = true,
            Importo = tentativo.ImportoIncassato ?? tentativo.Importo,
            Valuta = tentativo.Valuta,
            Motivo = testoMotivo,
            Stato = StatoRimborso.InCorso,
            IdUtenzaRichiedente = attore.Id,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };
        var impegnato = await RiprovaSuDeadlock.EseguiAsync(_contesto, async () =>
        {
            await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
            var righe = await _contesto.TentativiPagamento
                .Where(t => t.Id == idTentativo && t.Stato == StatoTentativoPagamento.DaRimborsare)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stato, StatoTentativoPagamento.RimborsoInCorso).SetProperty(t => t.AggiornatoIlUtc, adesso), annullamento);
            if (righe == 0)
            {
                return false;
            }

            _contesto.Rimborsi.Add(rimborso);
            await _contesto.SaveChangesAsync(annullamento);
            await transazione.CommitAsync(annullamento);
            return true;
        });
        if (!impegnato)
        {
            return RisultatoRimborso.NonRimborsabile;
        }

        await _audit.RegistraAsync(AzioniAudit.RichiestaRimborso, $"Ordine:{tentativo.IdOrdine}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, attore.Id, $"incasso {tentativo.IdProvider}: {rimborso.Importo:0.00} {tentativo.Valuta}", annullamento);
        return await EseguiAsync(rimborso, tentativo.IdCattura, credenziali, annullamento);
    }

    public async Task<RisultatoRimborso> VerificaAsync(Guid idAttore, Guid idRimborso, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            await RegistraNegazioneAsync(idAttore, "VerificaRimborso", annullamento);
            return RisultatoRimborso.NonAutorizzato;
        }

        var idSito = _tenant.IdSito;
        var rimborso = await _contesto.Rimborsi.AsNoTracking().SingleOrDefaultAsync(r => r.Id == idRimborso && r.IdSito == idSito, annullamento);
        if (rimborso is null)
        {
            return RisultatoRimborso.NonTrovato;
        }

        if (rimborso.Stato != StatoRimborso.InCorso)
        {
            return rimborso.Stato == StatoRimborso.Completato ? RisultatoRimborso.Completato : RisultatoRimborso.Rifiutato;
        }

        var cattura = await _contesto.TentativiPagamento.AsNoTracking().Where(t => t.Id == rimborso.IdTentativo).Select(t => t.IdCattura).SingleAsync(annullamento);
        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, idSito, annullamento);
        if (cattura is null || credenziali is null)
        {
            return RisultatoRimborso.NonConfigurato;
        }

        return await EseguiAsync(rimborso, cattura, credenziali, annullamento);
    }

    public async Task<bool> AllineaDaProviderAsync(string idRimborsoProvider, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var rimborso = await _contesto.Rimborsi.AsNoTracking().SingleOrDefaultAsync(r => r.IdSito == idSito && r.IdProvider == idRimborsoProvider, annullamento);
        if (rimborso is null)
        {
            return false;
        }

        if (rimborso.Stato != StatoRimborso.InCorso)
        {
            return true;
        }

        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, idSito, annullamento);
        if (credenziali is null)
        {
            return true;
        }

        EsitoRimborsoProvider esito;
        try
        {
            esito = await _provider.LeggiRimborsoAsync(credenziali, idRimborsoProvider, annullamento);
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, rimborso.Id, eccezione.Message);
            throw;
        }

        await ApplicaEsitoAsync(rimborso, esito, annullamento);
        return true;
    }

    public async Task<bool> RegistraRimborsoEsternoPayPalAsync(string idRimborsoProvider, CancellationToken annullamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idRimborsoProvider);

        var idSito = _tenant.IdSito;
        if (await _contesto.Rimborsi.AsNoTracking().AnyAsync(r => r.IdSito == idSito && r.IdProvider == idRimborsoProvider, annullamento))
        {
            return true;
        }

        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, idSito, annullamento);
        if (credenziali is null)
        {
            return false;
        }

        // Il contenuto del webhook non decide nulla: importo, esito e incasso si rileggono dal provider.
        EsitoRimborsoProvider esito;
        try
        {
            esito = await _provider.LeggiRimborsoAsync(credenziali, idRimborsoProvider, annullamento);
        }
        catch (ProviderPagamentoException eccezione) when (eccezione.CodiceHttp == 404)
        {
            // Rimborso che il provider non conosce per questo conto: non è nostro, nessun motivo di far ritentare la notifica.
            return false;
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, Guid.Empty, eccezione.Message);
            throw;
        }

        if (esito.Stato != StatoRimborsoProvider.Completato || esito.IdCattura is null || esito.Importo is not > 0 || esito.Valuta is null)
        {
            return false;
        }

        var tentativo = await _contesto.TentativiPagamento.AsNoTracking()
            .SingleOrDefaultAsync(t => t.IdSito == idSito && t.Provider == NomiProviderPagamento.PayPal && t.IdCattura == esito.IdCattura, annullamento);
        return tentativo is not null && await RegistraEsternoAsync(tentativo, idRimborsoProvider, esito.Importo.Value, esito.Valuta, annullamento);
    }

    public async Task<int> RegistraRimborsiEsterniAsync(Guid idTentativo, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var tentativo = await _contesto.TentativiPagamento.AsNoTracking().SingleOrDefaultAsync(t => t.Id == idTentativo && t.IdSito == idSito, annullamento);
        if (tentativo is null
            || !_gateway.TryGetValue(tentativo.Provider, out var gateway)
            || await gateway.OttieniCredenzialiAsync(_tenant.IdTenant, idSito, annullamento) is not { } credenziali)
        {
            return 0;
        }

        IReadOnlyList<RimborsoDelProvider> rimborsi;
        try
        {
            rimborsi = await gateway.LeggiRimborsiAsync(credenziali, tentativo.IdProvider, annullamento);
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, Guid.Empty, eccezione.Message);
            throw;
        }

        var registrati = 0;
        foreach (var rimborso in rimborsi)
        {
            if (await _contesto.Rimborsi.AsNoTracking().AnyAsync(r => r.IdSito == idSito && r.IdProvider == rimborso.IdRimborso, annullamento))
            {
                continue;
            }

            // Il tentativo si rilegge a ogni giro: un rimborso precedente può averne cambiato lo stato.
            var attuale = await _contesto.TentativiPagamento.AsNoTracking().SingleAsync(t => t.Id == idTentativo, annullamento);
            if (await RegistraEsternoAsync(attuale, rimborso.IdRimborso, rimborso.Importo, rimborso.Valuta, annullamento))
            {
                registrati++;
            }
        }

        return registrati;
    }

    /// <summary>
    /// Registra un rimborso già eseguito presso il provider (dal suo pannello) e ne applica l'effetto come per uno richiesto da qui. Vero se
    /// il rimborso è ora noto o va ignorato senza errore; falso mai per un errore transitorio (quello solleva).
    /// </summary>
    private Task<bool> RegistraEsternoAsync(TentativoPagamento tentativo, string idRimborsoProvider, decimal importo, string valuta, CancellationToken annullamento) =>
        RiprovaSuDeadlock.EseguiAsync(_contesto, () => RegistraEsternoUnaVoltaAsync(tentativo, idRimborsoProvider, importo, valuta, annullamento));

    private async Task<bool> RegistraEsternoUnaVoltaAsync(TentativoPagamento tentativo, string idRimborsoProvider, decimal importo, string valuta, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var idSito = _tenant.IdSito;
        if (importo <= 0 || decimal.Round(importo, 2) != importo || !string.Equals(valuta, tentativo.Valuta, StringComparison.Ordinal))
        {
            await _audit.RegistraAsync(AzioniAudit.RimborsoEsterno, $"Ordine:{tentativo.IdOrdine}", RegistroAudit.EsitoNegato, idTenant, null, "rimborso del provider con importo o valuta non validi: ignorato", annullamento);
            return true;
        }

        if (await _contesto.Rimborsi.AsNoTracking().AnyAsync(r => r.IdSito == idSito && r.IdProvider == idRimborsoProvider, annullamento))
        {
            return true;
        }

        // Un rimborso chiesto da qui la cui risposta non è ancora arrivata è lo stesso rimborso, non un secondo: lo si fa concludere.
        var inVolo = await _contesto.Rimborsi.AsNoTracking()
            .Where(r => r.IdTentativo == tentativo.Id && r.Stato == StatoRimborso.InCorso && r.IdProvider == null && r.Importo == importo)
            .OrderBy(r => r.CreatoIlUtc)
            .FirstOrDefaultAsync(annullamento);
        if (inVolo is not null)
        {
            await ApplicaEsitoUnaVoltaAsync(inVolo, new EsitoRimborsoProvider(StatoRimborsoProvider.Completato, idRimborsoProvider), annullamento);
            return true;
        }

        var orfano = tentativo.Stato is StatoTentativoPagamento.DaRimborsare or StatoTentativoPagamento.RimborsoInCorso;
        if (!orfano && tentativo.Stato != StatoTentativoPagamento.Catturato)
        {
            // Incasso già restituito per intero o mai concluso: nulla da aggiornare.
            return true;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var rimborso = new Rimborso
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = idTenant,
            IdSito = idSito,
            IdOrdine = tentativo.IdOrdine,
            IdTentativo = tentativo.Id,
            IncassoOrfano = orfano,
            Importo = importo,
            Valuta = valuta,
            Stato = StatoRimborso.Completato,
            IdProvider = idRimborsoProvider,
            IdUtenzaRichiedente = null,
            Esterno = true,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        };

        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        if (!orfano)
        {
            // L'importo si somma al rimborsato solo se resta entro il totale dell'ordine: oltre, il conto non torna e va guardato a mano.
            var applicato = await _contesto.Ordini
                .Where(o => o.Id == tentativo.IdOrdine && o.TotaleRimborsato + importo <= o.TotaleLordo)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TotaleRimborsato, o => o.TotaleRimborsato + importo).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
            if (applicato == 0)
            {
                await transazione.RollbackAsync(annullamento);
                await _audit.RegistraAsync(AzioniAudit.RimborsoEsterno, $"Ordine:{tentativo.IdOrdine}", RegistroAudit.EsitoNegato, idTenant, null, $"rimborso del provider di {importo:0.00} {valuta} oltre il residuo dell'ordine: non registrato", annullamento);
                return true;
            }
        }

        _contesto.Rimborsi.Add(rimborso);
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateException)
        {
            // Registrato nel frattempo da un'altra notifica uguale (indice unico sul rimborso del provider).
            await transazione.RollbackAsync(annullamento);
            _contesto.ChangeTracker.Clear();
            return true;
        }

        if (orfano)
        {
            // Incasso che non era diventato un ordine pagato: restituito per intero → chiuso.
            var restituito = await _contesto.Rimborsi.Where(r => r.IdTentativo == tentativo.Id && r.IncassoOrfano && r.Stato == StatoRimborso.Completato).SumAsync(r => r.Importo, annullamento);
            if (restituito >= (tentativo.ImportoIncassato ?? tentativo.Importo))
            {
                await _contesto.TentativiPagamento.Where(t => t.Id == tentativo.Id && (t.Stato == StatoTentativoPagamento.DaRimborsare || t.Stato == StatoTentativoPagamento.RimborsoInCorso))
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stato, StatoTentativoPagamento.Rimborsato).SetProperty(t => t.AggiornatoIlUtc, adesso), annullamento);
            }
        }
        else
        {
            var ordine = await _contesto.Ordini.AsNoTracking().SingleAsync(o => o.Id == tentativo.IdOrdine, annullamento);
            var completati = await _contesto.Rimborsi.Where(r => r.IdOrdine == tentativo.IdOrdine && !r.IncassoOrfano && r.Stato == StatoRimborso.Completato).SumAsync(r => r.Importo, annullamento);
            var totale = completati >= ordine.TotaleLordo;
            if (totale)
            {
                await _contesto.Ordini.Where(o => o.Id == tentativo.IdOrdine && _rimborsabili.Contains(o.Stato))
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Rimborsato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
            }

            await _notifiche.AccodaRimborsoAsync(ordine, rimborso, totale, annullamento);
            await _contesto.SaveChangesAsync(annullamento);
        }

        await transazione.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.RimborsoEsterno, $"Ordine:{tentativo.IdOrdine}", RegistroAudit.EsitoRiuscito, idTenant, null, $"rimborso {importo:0.00} {valuta} eseguito dal pannello del provider ({tentativo.Provider})", annullamento);
        return true;
    }

    public async Task<IReadOnlyList<RimborsoVista>> ElencaAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var idSito = _tenant.IdSito;
        return await _contesto.Rimborsi.AsNoTracking()
            .Where(r => r.IdOrdine == idOrdine && r.IdSito == idSito)
            .OrderBy(r => r.CreatoIlUtc)
            .Select(r => new RimborsoVista(r.Id, r.Importo, r.Valuta, r.Motivo, r.Stato, r.IncassoOrfano, r.CreatoIlUtc, r.Esterno))
            .ToListAsync(annullamento);
    }

    /// <summary>Chiama il provider e applica l'esito. Se l'esito è incerto (provider irraggiungibile) il rimborso resta «in corso» e l'importo impegnato.</summary>
    private async Task<RisultatoRimborso> EseguiAsync(Rimborso rimborso, string idCattura, CredenzialiPayPal credenziali, CancellationToken annullamento)
    {
        EsitoRimborsoProvider esito;
        try
        {
            esito = await _provider.RimborsaAsync(credenziali, idCattura, rimborso.Importo, rimborso.Valuta, rimborso.Id, rimborso.Motivo, annullamento);
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, rimborso.Id, eccezione.Message);
            return RisultatoRimborso.ErroreProvider;
        }

        return await ApplicaEsitoAsync(rimborso, esito, annullamento);
    }

    private Task<RisultatoRimborso> ApplicaEsitoAsync(Rimborso rimborso, EsitoRimborsoProvider esito, CancellationToken annullamento) =>
        RiprovaSuDeadlock.EseguiAsync(_contesto, () => ApplicaEsitoUnaVoltaAsync(rimborso, esito, annullamento));

    private async Task<RisultatoRimborso> ApplicaEsitoUnaVoltaAsync(Rimborso rimborso, EsitoRimborsoProvider esito, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var idTenant = _tenant.IdTenant;
        var id = rimborso.Id;

        if (esito.Stato == StatoRimborsoProvider.InSospeso)
        {
            var idProvider = esito.IdRimborso;
            if (idProvider is not null)
            {
                await _contesto.Rimborsi.Where(r => r.Id == id && r.Stato == StatoRimborso.InCorso && r.IdProvider == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.IdProvider, idProvider).SetProperty(r => r.AggiornatoIlUtc, adesso), annullamento);
            }

            return RisultatoRimborso.InCorso;
        }

        await using var transazione = await _contesto.Database.BeginTransactionAsync(annullamento);
        var nuovoStato = esito.Stato == StatoRimborsoProvider.Completato ? StatoRimborso.Completato : StatoRimborso.Fallito;
        var idRimborsoProvider = esito.IdRimborso;
        var cambiato = await _contesto.Rimborsi.Where(r => r.Id == id && r.Stato == StatoRimborso.InCorso)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Stato, nuovoStato).SetProperty(r => r.IdProvider, r => idRimborsoProvider ?? r.IdProvider).SetProperty(r => r.AggiornatoIlUtc, adesso), annullamento);
        if (cambiato == 0)
        {
            // Già concluso da un altro processo (ad esempio il webhook): si riporta com'è finito.
            await transazione.RollbackAsync(annullamento);
            var attuale = await _contesto.Rimborsi.AsNoTracking().Where(r => r.Id == id).Select(r => r.Stato).SingleAsync(annullamento);
            return attuale == StatoRimborso.Completato ? RisultatoRimborso.Completato : RisultatoRimborso.Rifiutato;
        }

        if (nuovoStato == StatoRimborso.Fallito)
        {
            if (rimborso.IncassoOrfano)
            {
                await _contesto.TentativiPagamento.Where(t => t.Id == rimborso.IdTentativo && t.Stato == StatoTentativoPagamento.RimborsoInCorso)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stato, StatoTentativoPagamento.DaRimborsare).SetProperty(t => t.AggiornatoIlUtc, adesso), annullamento);
            }
            else
            {
                var importo = rimborso.Importo;
                await _contesto.Ordini.Where(o => o.Id == rimborso.IdOrdine)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.TotaleRimborsato, o => o.TotaleRimborsato - importo).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
            }

            await transazione.CommitAsync(annullamento);
            await _audit.RegistraAsync(AzioniAudit.RimborsoRifiutato, $"Ordine:{rimborso.IdOrdine}", RegistroAudit.EsitoNegato, idTenant, null, $"rimborso {rimborso.Importo:0.00} {rimborso.Valuta} rifiutato dal provider", annullamento);
            return RisultatoRimborso.Rifiutato;
        }

        if (rimborso.IncassoOrfano)
        {
            await _contesto.TentativiPagamento.Where(t => t.Id == rimborso.IdTentativo && t.Stato == StatoTentativoPagamento.RimborsoInCorso)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stato, StatoTentativoPagamento.Rimborsato).SetProperty(t => t.AggiornatoIlUtc, adesso), annullamento);
        }
        else
        {
            // Rimborsato per intero quando i rimborsi completati coprono il totale: da quel momento l'ordine è chiuso.
            var ordine = await _contesto.Ordini.AsNoTracking().SingleAsync(o => o.Id == rimborso.IdOrdine, annullamento);
            var completati = await _contesto.Rimborsi.Where(r => r.IdOrdine == rimborso.IdOrdine && !r.IncassoOrfano && r.Stato == StatoRimborso.Completato).SumAsync(r => r.Importo, annullamento);
            var totale = completati >= ordine.TotaleLordo;
            if (totale)
            {
                await _contesto.Ordini.Where(o => o.Id == rimborso.IdOrdine && _rimborsabili.Contains(o.Stato))
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Rimborsato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
            }

            await _notifiche.AccodaRimborsoAsync(ordine, rimborso, totale, annullamento);
            await _contesto.SaveChangesAsync(annullamento);
        }

        await transazione.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.RimborsoCompletato, $"Ordine:{rimborso.IdOrdine}", RegistroAudit.EsitoRiuscito, idTenant, null, $"rimborso {rimborso.Importo:0.00} {rimborso.Valuta}", annullamento);
        return RisultatoRimborso.Completato;
    }

    private static string? PulisciMotivo(string? motivo, out bool valido)
    {
        var testo = motivo?.Trim();
        valido = testo is null or { Length: 0 } || (testo.Length <= Rimborso.LunghezzaMassimaMotivo && !testo.Any(char.IsControl));
        return string.IsNullOrEmpty(testo) ? null : testo;
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

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione, CancellationToken annullamento)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(c => c.Id == idAttore, annullamento);
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "Rimborso", RegistroAudit.EsitoNegato, _tenant.IdTenant, attoreEsiste ? idAttore : null, $"azione richiesta: {azione}", annullamento);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rimborso {IdRimborso}: errore del provider ({Dettaglio}); l'esito resta da verificare")]
    private static partial void RegistraErroreProvider(ILogger logger, Guid idRimborso, string dettaglio);
}

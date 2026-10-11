using System.Text.Json;
using DvCms.Application.Accessi;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Negozio;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Pagamenti;

/// <summary>Pagamenti con PayPal (vedi <see cref="IServizioPagamenti"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed partial class ServizioPagamenti : IServizioPagamenti
{
    private const string NomeProvider = "PayPal";

    private static readonly StatoTentativoPagamento[] _incassabili = [StatoTentativoPagamento.Creato, StatoTentativoPagamento.InSospeso, StatoTentativoPagamento.Fallito];

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IProviderPagamento _provider;
    private readonly IArchivioCredenzialiPagamento _credenziali;
    private readonly IServizioGiacenze _giacenze;
    private readonly IServizioAudit _audit;
    private readonly OpzioniNegozio _opzioni;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ServizioPagamenti> _logger;

    public ServizioPagamenti(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IProviderPagamento provider,
        IArchivioCredenzialiPagamento credenziali,
        IServizioGiacenze giacenze,
        IServizioAudit audit,
        OpzioniNegozio opzioni,
        TimeProvider orologio,
        ILogger<ServizioPagamenti> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _provider = provider;
        _credenziali = credenziali;
        _giacenze = giacenze;
        _audit = audit;
        _opzioni = opzioni;
        _orologio = orologio;
        _logger = logger;
    }

    public async Task<EsitoAvvioPagamento> AvviaAsync(Guid idAccount, Guid idOrdine, DatiLinkInvito link, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.NonAutorizzato, null);
        }

        await _giacenze.RilasciaScaduteAsync(annullamento);
        var idSito = _tenant.IdSito;
        var ordine = await _contesto.Ordini.AsNoTracking().SingleOrDefaultAsync(o => o.Id == idOrdine && o.IdSito == idSito && o.IdUtenza == idAccount, annullamento);
        if (ordine is null)
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.NonTrovato, null);
        }

        if (ordine.Stato is not (StatoOrdine.InAttesaDiPagamento or StatoOrdine.PagamentoFallito))
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.OrdineChiuso, null);
        }

        if (ordine.TotaleLordo <= 0)
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.NonDovuto, null);
        }

        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, idSito, annullamento);
        if (credenziali is null)
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.NonConfigurato, null);
        }

        if (credenziali.Ambiente == AmbientePagamento.Live && !_opzioni.CheckoutProduzioneAbilitato)
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.ProduzioneBloccata, null);
        }

        if (link.Schema is not ("http" or "https") || link.Porta is <= 0 or > 65535)
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.ErroreProvider, null);
        }

        // Un ordine con pagamento rifiutato torna in attesa per il nuovo tentativo (passaggio lecito e condizionale).
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        if (ordine.Stato == StatoOrdine.PagamentoFallito)
        {
            var ripreso = await _contesto.Ordini.Where(o => o.Id == idOrdine && o.Stato == StatoOrdine.PagamentoFallito)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.InAttesaDiPagamento).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
            if (ripreso == 0 && !await _contesto.Ordini.AnyAsync(o => o.Id == idOrdine && o.Stato == StatoOrdine.InAttesaDiPagamento, annullamento))
            {
                return new EsitoAvvioPagamento(RisultatoPagamento.OrdineChiuso, null);
            }
        }

        var baseUrl = $"{link.Schema}://{_tenant.Host}{(link.Porta is { } porta ? $":{porta}" : string.Empty)}";
        var idTentativo = Identificativi.Nuovo(_orologio);
        OrdineProvider creato;
        try
        {
            creato = await _provider.CreaOrdineAsync(
                credenziali,
                new RichiestaOrdineProvider(
                    idTentativo,
                    ordine.TotaleLordo,
                    ordine.Valuta,
                    ordine.Id.ToString("D"),
                    $"Ordine #{ordine.Numero}",
                    new Uri($"{baseUrl}/account/ordini/{ordine.Id:D}/pagamento"),
                    new Uri($"{baseUrl}/account/ordini/{ordine.Id:D}")),
                annullamento);
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, ordine.Id, eccezione.Message);
            return new EsitoAvvioPagamento(RisultatoPagamento.ErroreProvider, null);
        }

        _contesto.TentativiPagamento.Add(new TentativoPagamento
        {
            Id = idTentativo,
            IdTenant = _tenant.IdTenant,
            IdSito = idSito,
            IdOrdine = ordine.Id,
            Provider = NomeProvider,
            IdProvider = creato.IdProvider,
            Importo = ordine.TotaleLordo,
            Valuta = ordine.Valuta,
            Stato = StatoTentativoPagamento.Creato,
            CreatoIlUtc = adesso,
            AggiornatoIlUtc = adesso,
        });
        await _audit.RegistraAsync(AzioniAudit.AvvioPagamento, $"Ordine:{ordine.Id}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, idAccount, $"ordine {ordine.Numero}, {creato.IdProvider}", annullamento);
        return new EsitoAvvioPagamento(RisultatoPagamento.Avviato, creato.UrlApprovazione);
    }

    public Task<RisultatoPagamento> CompletaAsync(Guid idAccount, Guid idOrdine, string? idProvider, CancellationToken annullamento) =>
        RiprovaSuDeadlock.EseguiAsync(_contesto, () => CompletaUnaVoltaAsync(idAccount, idOrdine, idProvider, annullamento));

    private async Task<RisultatoPagamento> CompletaUnaVoltaAsync(Guid idAccount, Guid idOrdine, string? idProvider, CancellationToken annullamento)
    {
        if (await TrovaAccountAsync(idAccount, annullamento) is null)
        {
            return RisultatoPagamento.NonAutorizzato;
        }

        var idSito = _tenant.IdSito;
        var idTentativo = await _contesto.TentativiPagamento.AsNoTracking()
            .Where(t => t.IdOrdine == idOrdine && t.IdSito == idSito && t.Provider == NomeProvider && t.IdProvider == idProvider)
            .Join(_contesto.Ordini.Where(o => o.IdUtenza == idAccount), t => t.IdOrdine, o => o.Id, (t, _) => (Guid?)t.Id)
            .SingleOrDefaultAsync(annullamento);
        return idTentativo is null ? RisultatoPagamento.NonTrovato : await ElaboraTentativoAsync(idTentativo.Value, cattura: true, annullamento);
    }

    public async Task<EsitoWebhook> ElaboraWebhookAsync(IntestazioniWebhook intestazioni, string corpo, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(intestazioni);
        ArgumentNullException.ThrowIfNull(corpo);

        var idSito = _tenant.IdSito;
        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, idSito, annullamento);
        if (credenziali is null)
        {
            return EsitoWebhook.NonConfigurato;
        }

        // La firma si verifica prima di guardare il contenuto: un evento non firmato dal provider non ha alcun effetto.
        try
        {
            if (!await _provider.VerificaWebhookAsync(credenziali, intestazioni, corpo, annullamento))
            {
                return EsitoWebhook.FirmaNonValida;
            }
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreWebhook(_logger, eccezione.Message);
            return EsitoWebhook.ErroreProvider;
        }

        string? idEvento;
        string? tipo;
        string? idOrdineProvider;
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var radice = documento.RootElement;
            idEvento = Testo(radice, "id");
            tipo = Testo(radice, "event_type");
            idOrdineProvider = radice.TryGetProperty("resource", out var risorsa) && risorsa.ValueKind == JsonValueKind.Object
                ? (tipo is not null && tipo.StartsWith("CHECKOUT.ORDER.", StringComparison.Ordinal)
                    ? Testo(risorsa, "id")
                    : risorsa.TryGetProperty("supplementary_data", out var supplementari)
                        && supplementari.TryGetProperty("related_ids", out var collegati)
                        ? Testo(collegati, "order_id")
                        : null)
                : null;
        }
        catch (JsonException)
        {
            return EsitoWebhook.Malformato;
        }

        if (string.IsNullOrWhiteSpace(idEvento) || idEvento.Length > 100 || string.IsNullOrWhiteSpace(tipo) || tipo.Length > 100)
        {
            return EsitoWebhook.Malformato;
        }

        // Deduplica: lo stesso evento arriva più volte (ripetizioni del provider, risposte perse). Un evento già elaborato non fa nulla;
        // uno ricevuto ma mai concluso (interruzione a metà) si rielabora, perché ogni passaggio è idempotente.
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var evento = new EventoWebhook { Id = Identificativi.Nuovo(_orologio), IdTenant = _tenant.IdTenant, IdSito = idSito, Provider = NomeProvider, IdEvento = idEvento, Tipo = tipo, Esito = EventoWebhook.EsitoRicevuto, RicevutoIlUtc = adesso };
        _contesto.EventiWebhook.Add(evento);
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateException)
        {
            _contesto.ChangeTracker.Clear();
            var esistente = await _contesto.EventiWebhook.AsNoTracking().SingleOrDefaultAsync(e => e.IdSito == idSito && e.Provider == NomeProvider && e.IdEvento == idEvento, annullamento);
            if (esistente is null || esistente.Esito != EventoWebhook.EsitoRicevuto)
            {
                return EsitoWebhook.Duplicato;
            }

            evento = esistente;
        }

        string esito;
        var risultato = EsitoWebhook.Elaborato;
        switch (tipo)
        {
            case "CHECKOUT.ORDER.APPROVED" or "PAYMENT.CAPTURE.COMPLETED" or "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.DECLINED":
                var idTentativo = idOrdineProvider is null
                    ? null
                    : await _contesto.TentativiPagamento.AsNoTracking()
                        .Where(t => t.IdSito == idSito && t.Provider == NomeProvider && t.IdProvider == idOrdineProvider)
                        .Select(t => (Guid?)t.Id)
                        .SingleOrDefaultAsync(annullamento);
                if (idTentativo is null)
                {
                    // Pagamento che questo sito non conosce: si registra e si risponde bene, il provider non deve ripetere all'infinito.
                    esito = EventoWebhook.EsitoScartato;
                    risultato = EsitoWebhook.Ignorato;
                    break;
                }

                // Il contenuto del webhook non decide nulla: lo stato vero si chiede al provider (cattura per l'approvazione, lettura per il resto).
                var applicato = await ElaboraTentativoAsync(idTentativo.Value, cattura: tipo == "CHECKOUT.ORDER.APPROVED", annullamento);
                if (applicato == RisultatoPagamento.ErroreProvider)
                {
                    return EsitoWebhook.ErroreProvider;
                }

                esito = EventoWebhook.EsitoElaborato;
                break;
            default:
                esito = EventoWebhook.EsitoIgnorato;
                risultato = EsitoWebhook.Ignorato;
                break;
        }

        var idEventoRiga = evento.Id;
        await _contesto.EventiWebhook.IgnoreQueryFilters().Where(e => e.Id == idEventoRiga).ExecuteUpdateAsync(s => s.SetProperty(e => e.Esito, esito), annullamento);
        return risultato;
    }

    public async Task<IReadOnlyList<TentativoPagamentoVista>> ElencaTentativiAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        var autorizzato = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
        if (!autorizzato)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var idSito = _tenant.IdSito;
        return await _contesto.TentativiPagamento.AsNoTracking()
            .Where(t => t.IdOrdine == idOrdine && t.IdSito == idSito)
            .OrderBy(t => t.CreatoIlUtc)
            .Select(t => new TentativoPagamentoVista(t.Id, t.IdProvider, t.Stato, t.Importo, t.Valuta, t.CreatoIlUtc, t.AggiornatoIlUtc))
            .ToListAsync(annullamento);
    }

    private static string? Testo(JsonElement oggetto, string nome) =>
        oggetto.ValueKind == JsonValueKind.Object && oggetto.TryGetProperty(nome, out var valore) && valore.ValueKind == JsonValueKind.String ? valore.GetString() : null;

    /// <summary>Incassa (o legge) lo stato presso il provider per un tentativo e applica l'esito all'ordine.</summary>
    private Task<RisultatoPagamento> ElaboraTentativoAsync(Guid idTentativo, bool cattura, CancellationToken annullamento) =>
        RiprovaSuDeadlock.EseguiAsync(_contesto, () => ElaboraTentativoUnaVoltaAsync(idTentativo, cattura, annullamento));

    private async Task<RisultatoPagamento> ElaboraTentativoUnaVoltaAsync(Guid idTentativo, bool cattura, CancellationToken annullamento)
    {
        var tentativo = await _contesto.TentativiPagamento.AsNoTracking().SingleAsync(t => t.Id == idTentativo, annullamento);
        if (tentativo.Stato == StatoTentativoPagamento.Catturato)
        {
            return RisultatoPagamento.Pagato;
        }

        if (tentativo.Stato == StatoTentativoPagamento.DaRimborsare)
        {
            return RisultatoPagamento.DaRimborsare;
        }

        await _giacenze.RilasciaScaduteAsync(annullamento);
        var ordine = await _contesto.Ordini.AsNoTracking().SingleAsync(o => o.Id == tentativo.IdOrdine, annullamento);
        if (ordine.Stato == StatoOrdine.Pagato)
        {
            // Pagato con un altro tentativo: questo non incassa nulla.
            await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.Annullato, null, [StatoTentativoPagamento.Creato, StatoTentativoPagamento.InSospeso, StatoTentativoPagamento.Fallito], annullamento);
            return RisultatoPagamento.Pagato;
        }

        if (ordine.Stato is not (StatoOrdine.InAttesaDiPagamento or StatoOrdine.PagamentoFallito))
        {
            await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.Annullato, null, [StatoTentativoPagamento.Creato, StatoTentativoPagamento.InSospeso, StatoTentativoPagamento.Fallito], annullamento);
            return RisultatoPagamento.OrdineChiuso;
        }

        var credenziali = await _credenziali.OttieniAsync(_tenant.IdTenant, _tenant.IdSito, annullamento);
        if (credenziali is null)
        {
            return RisultatoPagamento.NonConfigurato;
        }

        EsitoCatturaProvider esito;
        try
        {
            esito = cattura
                ? await _provider.CatturaAsync(credenziali, tentativo.IdProvider, tentativo.Id, annullamento)
                : await _provider.LeggiOrdineAsync(credenziali, tentativo.IdProvider, annullamento);
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, ordine.Id, eccezione.Message);
            return RisultatoPagamento.ErroreProvider;
        }

        return await ApplicaEsitoAsync(tentativo, ordine, esito, annullamento);
    }

    private async Task<RisultatoPagamento> ApplicaEsitoAsync(TentativoPagamento tentativo, Ordine ordine, EsitoCatturaProvider esito, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var idTenant = _tenant.IdTenant;
        switch (esito.Stato)
        {
            case StatoCatturaProvider.NonApprovata:
                return RisultatoPagamento.NonApprovato;

            case StatoCatturaProvider.InSospeso:
                await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.InSospeso, esito.IdCattura, [StatoTentativoPagamento.Creato], annullamento);
                return RisultatoPagamento.InSospeso;

            case StatoCatturaProvider.Rifiutata:
                await using (var transazione = await _contesto.Database.BeginTransactionAsync(annullamento))
                {
                    await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.Fallito, null, [StatoTentativoPagamento.Creato, StatoTentativoPagamento.InSospeso], annullamento);
                    await _contesto.Ordini.Where(o => o.Id == ordine.Id && o.Stato == StatoOrdine.InAttesaDiPagamento)
                        .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.PagamentoFallito).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
                    await transazione.CommitAsync(annullamento);
                }

                await _audit.RegistraAsync(AzioniAudit.PagamentoRifiutato, $"Ordine:{ordine.Id}", RegistroAudit.EsitoNegato, idTenant, null, $"ordine {ordine.Numero}, {tentativo.IdProvider}", annullamento);
                return RisultatoPagamento.Fallito;

            default:
                break;
        }

        // Incassato. Prima di dichiarare pagato l'ordine si controlla che il provider abbia incassato esattamente quanto dovuto.
        if (esito.Importo != ordine.TotaleLordo || !string.Equals(esito.Valuta, ordine.Valuta, StringComparison.Ordinal))
        {
            await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.DaRimborsare, esito.IdCattura, _incassabili, annullamento);
            await _audit.RegistraAsync(AzioniAudit.PagamentoDaRimborsare, $"Ordine:{ordine.Id}", RegistroAudit.EsitoNegato, idTenant, null, $"ordine {ordine.Numero}: importo incassato diverso da quello dovuto", annullamento);
            return RisultatoPagamento.DaRimborsare;
        }

        await using var tx = await _contesto.Database.BeginTransactionAsync(annullamento);
        var presa = await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.Catturato, esito.IdCattura, _incassabili, annullamento);
        if (!presa)
        {
            // Un altro processo (ritorno del cliente o webhook) l'ha già trattato: si legge com'è finito.
            await tx.RollbackAsync(annullamento);
            var stato = await _contesto.TentativiPagamento.AsNoTracking().Where(t => t.Id == tentativo.Id).Select(t => t.Stato).SingleAsync(annullamento);
            return stato == StatoTentativoPagamento.Catturato ? RisultatoPagamento.Pagato : RisultatoPagamento.DaRimborsare;
        }

        var pagato = await _contesto.Ordini
            .Where(o => o.Id == ordine.Id && (o.Stato == StatoOrdine.InAttesaDiPagamento || o.Stato == StatoOrdine.PagamentoFallito))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Stato, StatoOrdine.Pagato).SetProperty(o => o.AggiornatoIlUtc, adesso), annullamento);
        var merceGarantita = pagato > 0
            && await OperazioniGiacenza.ConfermaAsync(_contesto, idTenant, _tenant.IdSito, ordine.Id, adesso, () => Identificativi.Nuovo(_orologio), annullamento);
        if (!merceGarantita)
        {
            // Incassato ma l'ordine è stato chiuso nel frattempo, o la merce non è più riservata: nulla va confermato, serve un rimborso.
            await tx.RollbackAsync(annullamento);
            _contesto.ChangeTracker.Clear();
            await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.DaRimborsare, esito.IdCattura, _incassabili, annullamento);
            await _audit.RegistraAsync(AzioniAudit.PagamentoDaRimborsare, $"Ordine:{ordine.Id}", RegistroAudit.EsitoNegato, idTenant, null, $"ordine {ordine.Numero}: incassato ma ordine chiuso o merce non più riservata", annullamento);
            return RisultatoPagamento.DaRimborsare;
        }

        await _contesto.SaveChangesAsync(annullamento);
        await tx.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.PagamentoOrdine, $"Ordine:{ordine.Id}", RegistroAudit.EsitoRiuscito, idTenant, null, $"ordine {ordine.Numero}, incasso {esito.IdCattura}", annullamento);
        return RisultatoPagamento.Pagato;
    }

    /// <summary>Cambia lo stato del tentativo solo se è ancora uno di quelli di partenza. Vero se l'ha cambiato questa chiamata.</summary>
    private async Task<bool> ImpostaTentativoAsync(Guid idTentativo, StatoTentativoPagamento nuovo, string? idCattura, StatoTentativoPagamento[] daStati, CancellationToken annullamento)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var cambiati = await _contesto.TentativiPagamento
            .Where(t => t.Id == idTentativo && daStati.Contains(t.Stato))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stato, nuovo).SetProperty(t => t.IdCattura, t => idCattura ?? t.IdCattura).SetProperty(t => t.AggiornatoIlUtc, adesso), annullamento);
        return cambiati > 0;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pagamento dell'ordine {IdOrdine}: errore del provider ({Dettaglio})")]
    private static partial void RegistraErroreProvider(ILogger logger, Guid idOrdine, string dettaglio);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook di pagamento: verifica della firma non riuscita per un errore del provider ({Dettaglio})")]
    private static partial void RegistraErroreWebhook(ILogger logger, string dettaglio);
}

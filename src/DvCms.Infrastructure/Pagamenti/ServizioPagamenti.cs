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

/// <summary>Pagamenti con PayPal e Nexi (vedi <see cref="IServizioPagamenti"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed partial class ServizioPagamenti : IServizioPagamenti
{
    /// <summary>Il provider dei webhook firmati (PayPal); le notifiche di Nexi hanno il loro percorso.</summary>
    private const string NomeProvider = NomiProviderPagamento.PayPal;

    /// <summary>Dove Nexi invia le notifiche server-to-server, sul dominio del sito (le credenziali sono per sito).</summary>
    public const string PercorsoNotificaNexi = "/webhooks/nexi";

    private static readonly StatoTentativoPagamento[] _incassabili = [StatoTentativoPagamento.Creato, StatoTentativoPagamento.InSospeso, StatoTentativoPagamento.Fallito];

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IProviderPagamento _provider;
    private readonly IArchivioCredenzialiPagamento _credenziali;
    private readonly Dictionary<string, IGatewayPagamento> _gateway;
    private readonly IServizioGiacenze _giacenze;
    private readonly IServizioAudit _audit;
    private readonly NotificheOrdine _notifiche;
    private readonly IServizioRimborsi _rimborsi;
    private readonly OpzioniNegozio _opzioni;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ServizioPagamenti> _logger;

    public ServizioPagamenti(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IProviderPagamento provider,
        IArchivioCredenzialiPagamento credenziali,
        IEnumerable<IGatewayPagamento> gateway,
        IServizioGiacenze giacenze,
        IServizioAudit audit,
        NotificheOrdine notifiche,
        IServizioRimborsi rimborsi,
        OpzioniNegozio opzioni,
        TimeProvider orologio,
        ILogger<ServizioPagamenti> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _provider = provider;
        _credenziali = credenziali;
        _gateway = gateway.ToDictionary(g => g.Nome, StringComparer.Ordinal);
        _giacenze = giacenze;
        _audit = audit;
        _notifiche = notifiche;
        _rimborsi = rimborsi;
        _opzioni = opzioni;
        _orologio = orologio;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ProviderDisponibiliAsync(CancellationToken annullamento)
    {
        var disponibili = new List<string>();
        foreach (var nome in NomiProviderPagamento.Tutti)
        {
            if (_gateway.TryGetValue(nome, out var gateway)
                && await gateway.OttieniCredenzialiAsync(_tenant.IdTenant, _tenant.IdSito, annullamento) is { } credenziali
                && (credenziali.Ambiente != AmbientePagamento.Live || _opzioni.CheckoutProduzioneAbilitato))
            {
                disponibili.Add(nome);
            }
        }

        return disponibili;
    }

    public async Task<EsitoAvvioPagamento> AvviaAsync(Guid idAccount, Guid idOrdine, string provider, DatiLinkInvito link, CancellationToken annullamento)
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

        if (provider is null || !_gateway.TryGetValue(provider, out var gateway))
        {
            return new EsitoAvvioPagamento(RisultatoPagamento.NonConfigurato, null);
        }

        var credenziali = await gateway.OttieniCredenzialiAsync(_tenant.IdTenant, idSito, annullamento);
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
            creato = await gateway.CreaOrdineAsync(
                credenziali,
                new RichiestaOrdineProvider(
                    idTentativo,
                    ordine.TotaleLordo,
                    ordine.Valuta,
                    ordine.Id.ToString("D"),
                    $"Ordine #{ordine.Numero}",
                    new Uri($"{baseUrl}/account/ordini/{ordine.Id:D}/pagamento"),
                    new Uri($"{baseUrl}/account/ordini/{ordine.Id:D}"),
                    ordine.Numero,
                    new Uri($"{baseUrl}{PercorsoNotificaNexi}"),
                    ordine.EmailAcquirente,
                    ordine.NomeAcquirente,
                    ordine.CodiceLingua),
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
            Provider = gateway.Nome,
            IdProvider = creato.IdProvider,
            HashTokenNotifica = creato.TokenSicurezza is null ? null : ImprontaToken(creato.TokenSicurezza),
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
            .Where(t => t.IdOrdine == idOrdine && t.IdSito == idSito && t.IdProvider == idProvider)
            .Join(_contesto.Ordini.Where(o => o.IdUtenza == idAccount), t => t.IdOrdine, o => o.Id, (t, _) => (Guid?)t.Id)
            .SingleOrDefaultAsync(annullamento);
        return idTentativo is null ? RisultatoPagamento.NonTrovato : await ElaboraTentativoAsync(idTentativo.Value, cattura: true, annullamento);
    }

    public async Task<int> RiconciliaAsync(CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var fino = adesso - RiconciliazioneEta.Minima;
        var da = adesso - RiconciliazioneEta.Massima;
        var aperti = await _contesto.TentativiPagamento.AsNoTracking()
            .Where(t => t.IdSito == idSito
                && (t.Stato == StatoTentativoPagamento.Creato || t.Stato == StatoTentativoPagamento.InSospeso)
                && t.CreatoIlUtc < fino && t.CreatoIlUtc > da)
            .OrderBy(t => t.CreatoIlUtc)
            .Select(t => t.Id)
            .Take(RiconciliazioneEta.Lotto)
            .ToListAsync(annullamento);

        var conclusi = 0;
        foreach (var id in aperti)
        {
            var esito = await ElaboraTentativoAsync(id, cattura: false, annullamento);
            if (esito is not (RisultatoPagamento.NonApprovato or RisultatoPagamento.ErroreProvider or RisultatoPagamento.NonConfigurato or RisultatoPagamento.InSospeso))
            {
                conclusi++;
            }
        }

        return conclusi;
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
        string? idRisorsa;
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var radice = documento.RootElement;
            idEvento = Testo(radice, "id");
            tipo = Testo(radice, "event_type");
            idRisorsa = radice.TryGetProperty("resource", out var risorsaEvento) ? Testo(risorsaEvento, "id") : null;
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
            case "PAYMENT.CAPTURE.REFUNDED" or "PAYMENT.REFUND.COMPLETED" or "PAYMENT.REFUND.FAILED" or "PAYMENT.REFUND.CANCELLED":
                // Il contenuto dell'evento non decide nulla: il rimborso si rilegge dal provider. Uno non richiesto da qui (ad esempio fatto
                // direttamente dal conto PayPal) non si conosce e si ignora.
                bool conosciuto;
                try
                {
                    conosciuto = idRisorsa is not null && await _rimborsi.AllineaDaProviderAsync(idRisorsa, annullamento);
                }
                catch (ProviderPagamentoException)
                {
                    return EsitoWebhook.ErroreProvider;
                }

                if (!conosciuto)
                {
                    esito = EventoWebhook.EsitoScartato;
                    risultato = EsitoWebhook.Ignorato;
                    break;
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

    public async Task<EsitoWebhook> ElaboraNotificaNexiAsync(string corpo, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(corpo);

        string? idEvento;
        string? token;
        string? idOrdineNexi;
        string tipo;
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var radice = documento.RootElement;
            idEvento = Testo(radice, "eventId");
            token = Testo(radice, "securityToken");
            var operazione = radice.ValueKind == JsonValueKind.Object && radice.TryGetProperty("operation", out var op) ? op : default;
            idOrdineNexi = Testo(operazione, "orderId");
            tipo = $"{Testo(operazione, "operationType")}.{Testo(operazione, "operationResult")}";
        }
        catch (JsonException)
        {
            return EsitoWebhook.Malformato;
        }

        if (string.IsNullOrWhiteSpace(idEvento) || idEvento.Length > 100 || string.IsNullOrWhiteSpace(token) || token.Length > 200
            || string.IsNullOrWhiteSpace(idOrdineNexi) || idOrdineNexi.Length > ClienteNexi.LunghezzaMassimaIdOrdine)
        {
            return EsitoWebhook.Malformato;
        }

        tipo = tipo.Length > 100 ? tipo[..100] : tipo;
        var idSito = _tenant.IdSito;
        var tentativo = await _contesto.TentativiPagamento.AsNoTracking()
            .Where(t => t.IdSito == idSito && t.Provider == NomiProviderPagamento.Nexi && t.IdProvider == idOrdineNexi)
            .Select(t => new { t.Id, t.HashTokenNotifica })
            .SingleOrDefaultAsync(annullamento);
        if (tentativo is null)
        {
            // Ordine che questo sito non conosce: nulla da fare, e il gateway non deve insistere.
            return EsitoWebhook.Ignorato;
        }

        // Il token ricevuto alla creazione del pagamento dimostra che la notifica viene dal gateway: senza, non ha alcun effetto.
        if (tentativo.HashTokenNotifica is null
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(ImprontaToken(token)), System.Text.Encoding.ASCII.GetBytes(tentativo.HashTokenNotifica)))
        {
            return EsitoWebhook.FirmaNonValida;
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var evento = new EventoWebhook { Id = Identificativi.Nuovo(_orologio), IdTenant = _tenant.IdTenant, IdSito = idSito, Provider = NomiProviderPagamento.Nexi, IdEvento = idEvento, Tipo = tipo, Esito = EventoWebhook.EsitoRicevuto, RicevutoIlUtc = adesso };
        _contesto.EventiWebhook.Add(evento);
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateException)
        {
            _contesto.ChangeTracker.Clear();
            var esistente = await _contesto.EventiWebhook.AsNoTracking().SingleOrDefaultAsync(e => e.IdSito == idSito && e.Provider == NomiProviderPagamento.Nexi && e.IdEvento == idEvento, annullamento);
            if (esistente is null || esistente.Esito != EventoWebhook.EsitoRicevuto)
            {
                return EsitoWebhook.Duplicato;
            }

            evento = esistente;
        }

        // Il contenuto della notifica non decide nulla: l'esito vero si rilegge dal gateway.
        var applicato = await ElaboraTentativoAsync(tentativo.Id, cattura: false, annullamento);
        if (applicato == RisultatoPagamento.ErroreProvider)
        {
            return EsitoWebhook.ErroreProvider;
        }

        if (applicato == RisultatoPagamento.NonConfigurato)
        {
            return EsitoWebhook.NonConfigurato;
        }

        var idEventoRiga = evento.Id;
        await _contesto.EventiWebhook.IgnoreQueryFilters().Where(e => e.Id == idEventoRiga).ExecuteUpdateAsync(s => s.SetProperty(e => e.Esito, EventoWebhook.EsitoElaborato), annullamento);
        return EsitoWebhook.Elaborato;
    }

    /// <summary>Impronta (SHA-256 esadecimale) di un token di notifica: di quello ricevuto dal gateway si conserva solo questa.</summary>
    internal static string ImprontaToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

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
            .Select(t => new TentativoPagamentoVista(t.Id, t.IdProvider, t.Stato, t.Importo, t.Valuta, t.CreatoIlUtc, t.AggiornatoIlUtc, t.Provider))
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
        if (ordine.Stato == StatoOrdine.Pagato || ordine.Stato is not (StatoOrdine.InAttesaDiPagamento or StatoOrdine.PagamentoFallito))
        {
            // Pagato con un altro tentativo, o ordine chiuso: questo tentativo non deve incassare nulla (la cattura non parte). Ma se si sta
            // solo leggendo (webhook, riconciliazione) il provider può aver già incassato — una cattura andata a buon fine di cui non si è
            // saputo nulla: in quel caso il denaro va restituito, non dimenticato.
            var chiuso = ordine.Stato == StatoOrdine.Pagato ? RisultatoPagamento.Pagato : RisultatoPagamento.OrdineChiuso;
            if (!cattura && await LeggiIncassoOrfanoAsync(tentativo, ordine, annullamento) is { } orfano)
            {
                return orfano;
            }

            await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.Annullato, null, [StatoTentativoPagamento.Creato, StatoTentativoPagamento.InSospeso, StatoTentativoPagamento.Fallito], annullamento);
            return chiuso;
        }

        if (!_gateway.TryGetValue(tentativo.Provider, out var gateway)
            || await gateway.OttieniCredenzialiAsync(_tenant.IdTenant, _tenant.IdSito, annullamento) is not { } credenziali)
        {
            return RisultatoPagamento.NonConfigurato;
        }

        EsitoCatturaProvider esito;
        try
        {
            esito = cattura
                ? await gateway.CatturaAsync(credenziali, tentativo.IdProvider, tentativo.Id, annullamento)
                : await gateway.LeggiOrdineAsync(credenziali, tentativo.IdProvider, annullamento);
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, ordine.Id, eccezione.Message);
            return RisultatoPagamento.ErroreProvider;
        }

        return await ApplicaEsitoAsync(tentativo, ordine, esito, annullamento);
    }

    /// <summary>
    /// Per un tentativo che non dovrebbe incassare (ordine già pagato o chiuso): chiede al provider com'è andata. Se ha incassato, il
    /// tentativo diventa «da rimborsare» (con l'importo davvero incassato) e restituisce quell'esito; altrimenti <c>null</c>. Un errore del
    /// provider non cambia nulla: la lettura si ripete alla prossima riconciliazione.
    /// </summary>
    private async Task<RisultatoPagamento?> LeggiIncassoOrfanoAsync(TentativoPagamento tentativo, Ordine ordine, CancellationToken annullamento)
    {
        if (!_gateway.TryGetValue(tentativo.Provider, out var gateway)
            || await gateway.OttieniCredenzialiAsync(_tenant.IdTenant, _tenant.IdSito, annullamento) is not { } credenziali)
        {
            return null;
        }

        EsitoCatturaProvider esito;
        try
        {
            esito = await gateway.LeggiOrdineAsync(credenziali, tentativo.IdProvider, annullamento);
        }
        catch (ProviderPagamentoException eccezione)
        {
            RegistraErroreProvider(_logger, ordine.Id, eccezione.Message);
            return RisultatoPagamento.ErroreProvider;
        }

        if (esito.Stato != StatoCatturaProvider.Completata)
        {
            return null;
        }

        await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.DaRimborsare, esito.IdCattura, _incassabili, annullamento, esito.Importo);
        await _audit.RegistraAsync(AzioniAudit.PagamentoDaRimborsare, $"Ordine:{ordine.Id}", RegistroAudit.EsitoNegato, _tenant.IdTenant, null, $"ordine {ordine.Numero}: incasso arrivato dopo la chiusura dell'ordine o su un ordine già pagato", annullamento);
        return RisultatoPagamento.DaRimborsare;
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
            await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.DaRimborsare, esito.IdCattura, _incassabili, annullamento, esito.Importo);
            await _audit.RegistraAsync(AzioniAudit.PagamentoDaRimborsare, $"Ordine:{ordine.Id}", RegistroAudit.EsitoNegato, idTenant, null, $"ordine {ordine.Numero}: importo incassato diverso da quello dovuto", annullamento);
            return RisultatoPagamento.DaRimborsare;
        }

        await using var tx = await _contesto.Database.BeginTransactionAsync(annullamento);
        var presa = await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.Catturato, esito.IdCattura, _incassabili, annullamento, esito.Importo);
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
            await ImpostaTentativoAsync(tentativo.Id, StatoTentativoPagamento.DaRimborsare, esito.IdCattura, _incassabili, annullamento, esito.Importo);
            await _audit.RegistraAsync(AzioniAudit.PagamentoDaRimborsare, $"Ordine:{ordine.Id}", RegistroAudit.EsitoNegato, idTenant, null, $"ordine {ordine.Numero}: incassato ma ordine chiuso o merce non più riservata", annullamento);
            return RisultatoPagamento.DaRimborsare;
        }

        // La conferma al cliente nasce nella stessa transazione del pagamento: o ci sono entrambi o nessuno dei due.
        await _notifiche.AccodaConfermaPagamentoAsync(ordine, annullamento);
        if (_opzioni.AvvisoNuovoOrdineAmministratori)
        {
            await _notifiche.AccodaAvvisoAmministratoriAsync(ordine, annullamento);
        }

        await _contesto.SaveChangesAsync(annullamento);
        await tx.CommitAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.PagamentoOrdine, $"Ordine:{ordine.Id}", RegistroAudit.EsitoRiuscito, idTenant, null, $"ordine {ordine.Numero}, incasso {esito.IdCattura}", annullamento);
        return RisultatoPagamento.Pagato;
    }

    /// <summary>Cambia lo stato del tentativo solo se è ancora uno di quelli di partenza. Vero se l'ha cambiato questa chiamata.</summary>
    private async Task<bool> ImpostaTentativoAsync(Guid idTentativo, StatoTentativoPagamento nuovo, string? idCattura, StatoTentativoPagamento[] daStati, CancellationToken annullamento, decimal? importoIncassato = null)
    {
        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var cambiati = await _contesto.TentativiPagamento
            .Where(t => t.Id == idTentativo && daStati.Contains(t.Stato))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stato, nuovo).SetProperty(t => t.IdCattura, t => idCattura ?? t.IdCattura).SetProperty(t => t.ImportoIncassato, t => importoIncassato ?? t.ImportoIncassato).SetProperty(t => t.AggiornatoIlUtc, adesso), annullamento);
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

/// <summary>Finestra della riconciliazione: un tentativo si rilegge dopo la scadenza della prenotazione (15 minuti, più un margine) e non oltre due settimane.</summary>
internal static class RiconciliazioneEta
{
    public static readonly TimeSpan Minima = TimeSpan.FromMinutes(20);

    public static readonly TimeSpan Massima = TimeSpan.FromDays(14);

    public const int Lotto = 50;
}

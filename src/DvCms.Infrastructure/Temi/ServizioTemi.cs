using System.Text.Json;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Cms;
using DvCms.Contracts.Temi;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Temi;
using DvCms.Data;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Temi;
using DvCms.Extender;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Temi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Temi;

/// <summary>Gestione admin del tema (vedi <see cref="IServizioTemi"/>).</summary>
public sealed class ServizioTemi : IServizioTemi
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly RegistroTemi _registro;
    private readonly IInvalidazioneCache _invalidazione;
    private readonly TimeProvider _orologio;

    public ServizioTemi(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, RegistroTemi registro, IInvalidazioneCache invalidazione, TimeProvider orologio)
    {
        _invalidazione = invalidazione;
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _registro = registro;
        _orologio = orologio;
    }

    public async Task<ConfigurazioneTema> LeggiAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var idSito = _tenant.IdSito;
        var riga = await _contesto.ImpostazioniTemaSito.AsNoTracking().SingleOrDefaultAsync(candidata => candidata.IdSito == idSito, annullamento);
        var tema = riga is null ? null : _registro.Trova(riga.ChiaveTema, riga.VersioneTema);
        return tema is null || riga is null
            ? new ConfigurazioneTema(_registro.Predefinito.Chiave, _registro.Predefinito.Versione, new Dictionary<string, string>(), string.Empty, true, _registro.Ultimi)
            : new ConfigurazioneTema(tema.Chiave, tema.Versione, riga.Impostazioni, Convert.ToBase64String(riga.TokenConcorrenza), false, _registro.Ultimi);
    }

    public async Task<EsitoOperazione> SalvaAsync(
        Guid idAttore,
        string? chiave,
        IReadOnlyDictionary<string, string>? valori,
        string? tokenConcorrenza,
        CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        var riga = await _contesto.ImpostazioniTemaSito.SingleOrDefaultAsync(candidata => candidata.IdSito == idSito, annullamento);
        var attuale = riga is null ? null : _registro.Trova(riga.ChiaveTema, riga.VersioneTema);

        // Stesso tema: si resta sulla versione già configurata. Tema diverso (o primo salvataggio): ultima versione.
        var chiaveRichiesta = chiave?.Trim();
        var tema = attuale is not null && attuale.Chiave == chiaveRichiesta ? attuale : _registro.Ultimo(chiaveRichiesta);
        if (tema is null)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Tema"]);
        }

        var errori = _registro.Valida(tema.Chiave, tema.Versione, valori);
        if (errori.Count > 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, errori);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var normalizzati = JsonSerializer.Serialize(_registro.Normalizza(tema.Chiave, tema.Versione, valori));
        if (riga is null)
        {
            riga = new ImpostazioniTemaSito { Id = Identificativi.Nuovo(_orologio), IdTenant = _tenant.IdTenant, IdSito = idSito };
            _contesto.ImpostazioniTemaSito.Add(riga);
        }
        else
        {
            if (!TentaToken(tokenConcorrenza, out var token))
            {
                return new EsitoOperazione(CodiceEsito.Conflitto);
            }

            _contesto.Entry(riga).Property(candidata => candidata.TokenConcorrenza).OriginalValue = token;
        }

        riga.ChiaveTema = tema.Chiave;
        riga.VersioneTema = tema.Versione;
        riga.ImpostazioniJson = normalizzati;
        riga.AggiornatoIlUtc = adesso;
        riga.AggiornatoDaIdUtenza = attore.Id;
        _invalidazione.Registra(_tenant.IdTenant, idSito);

        try
        {
            await _audit.RegistraAsync(
                AzioniAudit.ModificaTema,
                $"Sito:{idSito}",
                RegistroAudit.EsitoRiuscito,
                _tenant.IdTenant,
                attore.Id,
                $"tema {tema.Chiave} versione {tema.Versione}",
                annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, riga.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
        catch (DbUpdateException)
        {
            // Due primi salvataggi in parallelo: l'indice univoco sul sito lascia passare uno solo.
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
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
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "Tema",
            RegistroAudit.EsitoNegato,
            _tenant.IdTenant,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {AzioniAudit.ModificaTema}",
            CancellationToken.None);
    }

    private static bool TentaToken(string? testo, out byte[] token)
    {
        token = [];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return false;
        }

        try
        {
            token = Convert.FromBase64String(testo);
            return token.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Risolve il tema del sito dell'host; un tema sconosciuto o una riga illeggibile ripiegano sul predefinito.</summary>
public sealed partial class RisolutoreTema : IRisolutoreTema
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly RegistroTemi _registro;
    private readonly ILogger<RisolutoreTema> _logger;

    public RisolutoreTema(ContestoDatabase contesto, IContestoTenant tenant, RegistroTemi registro, ILogger<RisolutoreTema> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _registro = registro;
        _logger = logger;
    }

    public async Task<TemaRisolto> RisolviAsync(CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var nomeSito = await _contesto.Siti.AsNoTracking().Where(sito => sito.Id == idSito).Select(sito => sito.Nome).SingleOrDefaultAsync(annullamento) ?? string.Empty;
        var riga = await _contesto.ImpostazioniTemaSito.AsNoTracking().SingleOrDefaultAsync(candidata => candidata.IdSito == idSito, annullamento);

        var tema = riga is null ? null : _registro.Trova(riga.ChiaveTema, riga.VersioneTema);
        if (riga is not null && tema is null)
        {
            RegistraTemaSconosciuto(_logger, idSito);
        }

        tema ??= _registro.Predefinito;
        var valori = RegistroTemi.Effettivi(tema, tema.Chiave == riga?.ChiaveTema && tema.Versione == riga.VersioneTema ? riga.Impostazioni : null);
        return new TemaRisolto(tema.Chiave, tema.Versione, nomeSito, valori, RegistroTemi.VariabiliCss(tema, valori));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Il sito {IdSito} ha un tema che non è nel registro: si usa il tema predefinito")]
    private static partial void RegistraTemaSconosciuto(ILogger logger, Guid idSito);
}

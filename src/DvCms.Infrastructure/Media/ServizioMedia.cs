using System.Security.Cryptography;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Media;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Media;
using DvCms.DTO.Media;
using DvCms.DTO.Piattaforma;
using DvCms.Extender;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models;
using DvCms.Models.Accessi;
using DvCms.Models.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Media;

/// <summary>Libreria media (vedi <see cref="IServizioMedia"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed partial class ServizioMedia : IServizioMedia
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IArchivioOggetti _archivio;
    private readonly IServizioAudit _audit;
    private readonly OpzioniMedia _opzioni;
    private readonly TimeProvider _orologio;
    private readonly ILogger<ServizioMedia> _logger;

    public ServizioMedia(
        ContestoDatabase contesto,
        IContestoTenant tenant,
        IArchivioOggetti archivio,
        IServizioAudit audit,
        OpzioniMedia opzioni,
        TimeProvider orologio,
        ILogger<ServizioMedia> logger)
    {
        _contesto = contesto;
        _tenant = tenant;
        _archivio = archivio;
        _audit = audit;
        _opzioni = opzioni;
        _orologio = orologio;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RiepilogoMedia>> ElencaAsync(Guid idAttore, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        var idSito = _tenant.IdSito;
        var lingua = (await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento)).FirstOrDefault(l => l.Predefinita);

        var risorse = await _contesto.RisorseMedia.AsNoTracking().Where(r => r.IdSito == idSito).OrderByDescending(r => r.CreatoIlUtc).ToListAsync(annullamento);
        var testi = lingua is null
            ? []
            : await _contesto.TraduzioniRisorseMedia.AsNoTracking()
                .Where(t => t.IdSito == idSito && t.IdLingua == lingua.IdLingua)
                .ToDictionaryAsync(t => t.IdRisorsa, t => t.TestoAlternativo, annullamento);
        return [.. risorse.Select(r => Riepilogo(r, testi.GetValueOrDefault(r.Id)))];
    }

    public async Task<DettaglioMedia?> LeggiAsync(Guid idAttore, Guid idRisorsa, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        var idSito = _tenant.IdSito;
        var risorsa = await _contesto.RisorseMedia.AsNoTracking().SingleOrDefaultAsync(r => r.Id == idRisorsa && r.IdSito == idSito, annullamento);
        if (risorsa is null)
        {
            return null;
        }

        var lingue = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var testi = await _contesto.TraduzioniRisorseMedia.AsNoTracking().Where(t => t.IdRisorsa == idRisorsa).ToDictionaryAsync(t => t.IdLingua, t => t.TestoAlternativo, annullamento);
        var predefinita = lingue.FirstOrDefault(l => l.Predefinita);
        return new DettaglioMedia(
            Riepilogo(risorsa, predefinita is null ? null : testi.GetValueOrDefault(predefinita.IdLingua)),
            Convert.ToHexString(risorsa.HashSha256).ToLowerInvariant(),
            Convert.ToBase64String(risorsa.TokenConcorrenza),
            [.. lingue.Select(l => new TestoAlternativoMedia(l.Codice, l.Nome, testi.GetValueOrDefault(l.IdLingua) ?? string.Empty))]);
    }

    public async Task<EsitoOperazione> CaricaAsync(Guid idAttore, FileDaCaricare file, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(file);

        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "CaricamentoMedia");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        // 1. Il contenuto, letto fino al limite (+1 byte per accorgersi che lo supera): mai di più in memoria.
        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        int letti;
        while ((letti = await file.Contenuto.ReadAsync(buffer, annullamento)) > 0)
        {
            if (memoria.Length + letti > _opzioni.DimensioneMassima)
            {
                return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Dimensione"]);
            }

            memoria.Write(buffer, 0, letti);
        }

        if (memoria.Length == 0)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["File"]);
        }

        // 2. Il tipo lo decide il contenuto; l'estensione deve essere ammessa e coerente; il tipo dichiarato dal client si ignora.
        var nome = PulisciNome(file.NomeOriginale);
        var estensione = Path.GetExtension(nome);
        if (!RilevatoreImmagini.EstensioniAmmesse.TryGetValue(estensione, out var tipoAtteso))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Tipo"]);
        }

        var immagine = RilevatoreImmagini.Rileva(memoria.GetBuffer().AsSpan(0, (int)memoria.Length));
        if (immagine is null || immagine.TipoContenuto != tipoAtteso)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Tipo"]);
        }

        if (immagine.Larghezza > _opzioni.LatoMassimoImmagine
            || immagine.Altezza > _opzioni.LatoMassimoImmagine
            || (long)immagine.Larghezza * immagine.Altezza > _opzioni.PixelMassimi)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Dimensioni"]);
        }

        // 3. Metadati e oggetto: se il salvataggio sul database fallisce l'oggetto appena scritto si toglie.
        var id = Identificativi.Nuovo(_orologio);
        var risorsa = new RisorsaMedia
        {
            Id = id,
            IdTenant = _tenant.IdTenant,
            IdSito = _tenant.IdSito,
            ChiaveOggetto = RisorsaMedia.CreaChiave(_tenant.IdTenant, _tenant.IdSito, id),
            NomeOriginale = nome,
            TipoContenuto = immagine.TipoContenuto,
            Dimensione = memoria.Length,
            HashSha256 = SHA256.HashData(memoria.GetBuffer().AsSpan(0, (int)memoria.Length)),
            Larghezza = immagine.Larghezza,
            Altezza = immagine.Altezza,
            CreatoIlUtc = _orologio.GetUtcNow().UtcDateTime,
            CreatoDaIdUtenza = attore.Id,
        };

        memoria.Position = 0;
        await _archivio.ScriviAsync(risorsa.ChiaveOggetto, memoria, annullamento);
        try
        {
            _contesto.RisorseMedia.Add(risorsa);
            await _audit.RegistraAsync(
                AzioniAudit.CaricamentoMedia,
                $"Media:{id}",
                RegistroAudit.EsitoRiuscito,
                _tenant.IdTenant,
                attore.Id,
                $"{risorsa.TipoContenuto}, {risorsa.Dimensione} byte",
                annullamento);
        }
        catch
        {
            await _archivio.EliminaAsync(risorsa.ChiaveOggetto, CancellationToken.None);
            throw;
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, id);
    }

    public async Task<EsitoOperazione> ImpostaTestoAlternativoAsync(Guid idAttore, Guid idRisorsa, string? codiceLingua, string? testo, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            await RegistraNegazioneAsync(idAttore, "TestoAlternativoMedia");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var valore = testo?.Trim() ?? string.Empty;
        if (valore.Length > RisorsaMedia.LunghezzaMassimaTestoAlternativo || valore.Any(char.IsControl))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["TestoAlternativo"]);
        }

        var idSito = _tenant.IdSito;
        var risorsa = await _contesto.RisorseMedia.AsNoTracking().SingleOrDefaultAsync(r => r.Id == idRisorsa && r.IdSito == idSito, annullamento);
        if (risorsa is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        var lingua = (await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento))
            .FirstOrDefault(l => string.Equals(l.Codice, codiceLingua?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (lingua is null)
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi, null, ["Lingua"]);
        }

        var esistente = await _contesto.TraduzioniRisorseMedia.SingleOrDefaultAsync(t => t.IdRisorsa == idRisorsa && t.IdLingua == lingua.IdLingua, annullamento);
        if (valore.Length == 0)
        {
            if (esistente is not null)
            {
                _contesto.TraduzioniRisorseMedia.Remove(esistente);
            }
        }
        else if (esistente is null)
        {
            _contesto.TraduzioniRisorseMedia.Add(new TraduzioneRisorsaMedia
            {
                IdTenant = risorsa.IdTenant,
                IdSito = risorsa.IdSito,
                IdRisorsa = idRisorsa,
                IdLingua = lingua.IdLingua,
                TestoAlternativo = valore,
            });
        }
        else
        {
            esistente.TestoAlternativo = valore;
        }

        return await SalvaAsync(risorsa.Id, annullamento);
    }

    public async Task<EsitoOperazione> ImpostaVisibilitaAsync(Guid idAttore, Guid idRisorsa, VisibilitaMedia visibilita, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "VisibilitaMedia");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!Enum.IsDefined(visibilita))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi);
        }

        var idSito = _tenant.IdSito;
        var risorsa = await _contesto.RisorseMedia.SingleOrDefaultAsync(r => r.Id == idRisorsa && r.IdSito == idSito, annullamento);
        if (risorsa is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        risorsa.Visibilita = visibilita;
        return await SalvaAsync(risorsa.Id, annullamento);
    }

    public async Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idRisorsa, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "EliminazioneMedia");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var idSito = _tenant.IdSito;
        var risorsa = await _contesto.RisorseMedia.SingleOrDefaultAsync(r => r.Id == idRisorsa && r.IdSito == idSito, annullamento);
        if (risorsa is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        _contesto.TraduzioniRisorseMedia.RemoveRange(await _contesto.TraduzioniRisorseMedia.Where(t => t.IdRisorsa == idRisorsa).ToListAsync(annullamento));
        _contesto.RisorseMedia.Remove(risorsa);
        try
        {
            await _audit.RegistraAsync(AzioniAudit.EliminazioneMedia, $"Media:{risorsa.Id}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, attore.Id, risorsa.TipoContenuto, annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }

        // Prima il database, poi l'oggetto: se la cancellazione dell'oggetto fallisce resta un file orfano (non raggiungibile), non un riferimento rotto.
        try
        {
            await _archivio.EliminaAsync(risorsa.ChiaveOggetto, annullamento);
        }
        catch (IOException eccezione)
        {
            RegistraOggettoOrfano(_logger, risorsa.Id, eccezione.GetType().Name);
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, risorsa.Id);
    }

    public async Task<ContenutoMedia?> ApriPerAmministratoreAsync(Guid idAttore, Guid idRisorsa, CancellationToken annullamento)
    {
        await RichiediAttoreAsync(idAttore, annullamento);
        var idSito = _tenant.IdSito;
        var risorsa = await _contesto.RisorseMedia.AsNoTracking().SingleOrDefaultAsync(r => r.Id == idRisorsa && r.IdSito == idSito, annullamento);
        return risorsa is null ? null : await ApriAsync(risorsa, annullamento);
    }

    public async Task<ContenutoMedia?> ApriPubblicaAsync(Guid idRisorsa, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var risorsa = await _contesto.RisorseMedia.AsNoTracking().SingleOrDefaultAsync(
            r => r.Id == idRisorsa
                && r.IdSito == idSito
                && r.Visibilita == VisibilitaMedia.Pubblica
                && r.StatoScansione != StatoScansioneMedia.InQuarantena,
            annullamento);
        return risorsa is null ? null : await ApriAsync(risorsa, annullamento);
    }

    private static string PulisciNome(string? nome)
    {
        // Solo il nome del file: niente percorsi (anche con barre rovesciate), niente caratteri di controllo.
        var ultimo = (nome ?? string.Empty).Split('/', '\\')[^1];
        var pulito = new string([.. ultimo.Where(c => !char.IsControl(c))]).Trim();
        if (pulito.Length > RisorsaMedia.LunghezzaMassimaNome)
        {
            var estensione = Path.GetExtension(pulito);
            pulito = pulito[..(RisorsaMedia.LunghezzaMassimaNome - estensione.Length)] + estensione;
        }

        return pulito;
    }

    private static RiepilogoMedia Riepilogo(RisorsaMedia r, string? testoAlternativo) =>
        new(r.Id, r.NomeOriginale, r.TipoContenuto, r.Dimensione, r.Larghezza, r.Altezza, r.Visibilita, r.StatoScansione, r.CreatoIlUtc, testoAlternativo);

    private async Task<ContenutoMedia?> ApriAsync(RisorsaMedia risorsa, CancellationToken annullamento)
    {
        var flusso = await _archivio.ApriAsync(risorsa.ChiaveOggetto, annullamento);
        return flusso is null
            ? null
            : new ContenutoMedia(flusso, risorsa.TipoContenuto, risorsa.Dimensione, Convert.ToHexString(risorsa.HashSha256).ToLowerInvariant(), risorsa.NomeOriginale);
    }

    private async Task<EsitoOperazione> SalvaAsync(Guid id, CancellationToken annullamento)
    {
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
            return new EsitoOperazione(CodiceEsito.Riuscito, id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
        catch (DbUpdateException)
        {
            return new EsitoOperazione(CodiceEsito.Conflitto);
        }
    }

    private async Task RichiediAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
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

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(
            AzioniAudit.OperazioneNegata,
            "Media",
            RegistroAudit.EsitoNegato,
            _tenant.IdTenant,
            attoreEsiste ? idAttore : null,
            $"azione richiesta: {azione}",
            CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Risorsa media {IdRisorsa} eliminata dal database ma non dall'archivio ({TipoEccezione}): oggetto orfano da ripulire")]
    private static partial void RegistraOggettoOrfano(ILogger logger, Guid idRisorsa, string tipoEccezione);
}

using System.Globalization;
using System.Text;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Moduli;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Moduli;
using DvCms.Data;
using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Accessi;
using DvCms.Models.Moduli;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Cms.Moduli;

/// <summary>
/// Casella delle richieste (vedi <see cref="IServizioRichiesteModulo"/>). L'esportazione è pensata per Excel in italiano: UTF-8 con BOM,
/// separatore <c>;</c>; i testi che iniziano con <c>= + - @</c> o con un carattere di controllo sono preceduti da un apice, perché un foglio
/// di calcolo non li esegua come formule (il visitatore sceglie liberamente ciò che scrive).
/// </summary>
public sealed class ServizioRichiesteModulo : IServizioRichiesteModulo
{
    private const int LunghezzaAnteprima = 80;

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioRichiesteModulo(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<ElencoRichieste?> ElencaAsync(Guid idAttore, Guid idModulo, StatoInvioModulo? stato, int pagina, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var modulo = await TrovaModuloAsync(idModulo, annullamento);
        if (modulo is null)
        {
            return null;
        }

        if (stato is { } richiesto && !Enum.IsDefined(richiesto))
        {
            stato = null;
        }

        var tutte = _contesto.InviiModulo.AsNoTracking().Where(i => i.IdModulo == idModulo && i.IdSito == modulo.IdSito);
        var nuove = await tutte.CountAsync(i => i.Stato == StatoInvioModulo.Nuovo, annullamento);
        var filtrate = stato is { } s ? tutte.Where(i => i.Stato == s) : tutte;
        var totale = await filtrate.CountAsync(annullamento);
        var dimensione = ElencoRichieste.DimensionePredefinita;
        var ultima = Math.Max(1, (int)Math.Ceiling(totale / (double)dimensione));
        pagina = Math.Clamp(pagina, 1, ultima);
        var righe = await filtrate
            .OrderByDescending(i => i.CreatoIlUtc)
            .ThenByDescending(i => i.Id)
            .Skip((pagina - 1) * dimensione)
            .Take(dimensione)
            .ToListAsync(annullamento);
        return new ElencoRichieste(
            idModulo,
            modulo.Nome,
            [.. righe.Select(i => new RiepilogoRichiesta(i.Id, i.Stato, i.CodiceLingua, i.CreatoIlUtc, Anteprima(SerializzatoreModulo.LeggiContenuto(i.ContenutoJson))))],
            totale,
            nuove,
            pagina,
            dimensione,
            stato);
    }

    public async Task<VistaRichiesta?> LeggiAsync(Guid idAttore, Guid idModulo, Guid idRichiesta, CancellationToken annullamento)
    {
        if (await TrovaAttoreAsync(idAttore, annullamento) is null)
        {
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var modulo = await TrovaModuloAsync(idModulo, annullamento);
        var invio = modulo is null
            ? null
            : await _contesto.InviiModulo.AsNoTracking().SingleOrDefaultAsync(i => i.Id == idRichiesta && i.IdModulo == idModulo && i.IdSito == modulo.IdSito, annullamento);
        return invio is null || modulo is null
            ? null
            : new VistaRichiesta(invio.Id, idModulo, modulo.Nome, invio.Stato, invio.CodiceLingua, invio.CreatoIlUtc, SerializzatoreModulo.LeggiContenuto(invio.ContenutoJson));
    }

    public async Task<EsitoOperazione> ImpostaStatoAsync(Guid idAttore, Guid idModulo, Guid idRichiesta, StatoInvioModulo stato, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, "CambioStatoRichiesta");
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        if (!Enum.IsDefined(stato))
        {
            return new EsitoOperazione(CodiceEsito.DatiNonValidi);
        }

        var invio = await TrovaInvioAsync(idModulo, idRichiesta, annullamento);
        if (invio is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        if (invio.Stato != stato)
        {
            invio.Stato = stato;
            await _contesto.SaveChangesAsync(annullamento);
        }

        return new EsitoOperazione(CodiceEsito.Riuscito, invio.Id);
    }

    public async Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idModulo, Guid idRichiesta, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.EliminazioneInvioModulo);
            return new EsitoOperazione(CodiceEsito.NonAutorizzato);
        }

        var invio = await TrovaInvioAsync(idModulo, idRichiesta, annullamento);
        if (invio is null)
        {
            return new EsitoOperazione(CodiceEsito.NonTrovato);
        }

        _contesto.InviiModulo.Remove(invio);
        await _contesto.SaveChangesAsync(annullamento);
        await _audit.RegistraAsync(AzioniAudit.EliminazioneInvioModulo, $"Modulo:{idModulo}", RegistroAudit.EsitoRiuscito, _tenant.IdTenant, attore.Id, $"richiesta {idRichiesta}", annullamento);
        return new EsitoOperazione(CodiceEsito.Riuscito, idRichiesta);
    }

    public async Task<EsportazioneRichieste?> EsportaAsync(Guid idAttore, Guid idModulo, StatoInvioModulo? stato, CancellationToken annullamento)
    {
        var attore = await TrovaAttoreAsync(idAttore, annullamento);
        if (attore is null)
        {
            await RegistraNegazioneAsync(idAttore, AzioniAudit.EsportazioneModulo);
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        var modulo = await TrovaModuloAsync(idModulo, annullamento);
        if (modulo is null)
        {
            return null;
        }

        var richieste = _contesto.InviiModulo.AsNoTracking().Where(i => i.IdModulo == idModulo && i.IdSito == modulo.IdSito);
        if (stato is { } richiesto && Enum.IsDefined(richiesto))
        {
            richieste = richieste.Where(i => i.Stato == richiesto);
        }
        else
        {
            stato = null;
        }

        var elenco = await richieste.OrderBy(i => i.CreatoIlUtc).ThenBy(i => i.Id).Take(IServizioRichiesteModulo.MassimoEsportabili + 1).ToListAsync(annullamento);
        var troncata = elenco.Count > IServizioRichiesteModulo.MassimoEsportabili;
        if (troncata)
        {
            elenco.RemoveAt(elenco.Count - 1);
        }

        // Le colonne sono l'unione dei campi incontrati, nell'ordine in cui compaiono: un modulo modificato nel tempo non perde nulla.
        var contenuti = elenco.Select(i => (Invio: i, Valori: SerializzatoreModulo.LeggiContenuto(i.ContenutoJson))).ToList();
        var colonne = new List<(string Id, string Etichetta)>();
        foreach (var (_, valori) in contenuti)
        {
            foreach (var valore in valori.Where(v => !colonne.Any(c => c.Id == v.Id)))
            {
                colonne.Add((valore.Id, valore.Etichetta));
            }
        }

        var csv = new StringBuilder();
        Riga(csv, ["Id", "DataUtc", "Stato", "Lingua", .. colonne.Select(c => Testo(c.Etichetta))]);
        foreach (var (invio, valori) in contenuti)
        {
            Riga(
                csv,
                [
                    invio.Id.ToString("D"),
                    invio.CreatoIlUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    invio.Stato.ToString(),
                    invio.CodiceLingua,
                    .. colonne.Select(c => Testo(string.Join(", ", valori.FirstOrDefault(v => v.Id == c.Id)?.Valori ?? []))),
                ]);
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var nomeFile = string.Create(CultureInfo.InvariantCulture, $"richieste-{adesso:yyyyMMdd-HHmmss}.csv");
        var contenuto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        await _audit.RegistraAsync(
            AzioniAudit.EsportazioneModulo,
            $"Modulo:{idModulo}",
            RegistroAudit.EsitoRiuscito,
            _tenant.IdTenant,
            attore.Id,
            string.Create(CultureInfo.InvariantCulture, $"formato CSV; righe {elenco.Count}; stato {stato?.ToString() ?? "-"}{(troncata ? "; troncata" : string.Empty)}"),
            annullamento);
        return new EsportazioneRichieste(nomeFile, contenuto, elenco.Count, troncata);
    }

    private static string Anteprima(IReadOnlyList<ValoreInviato> contenuto)
    {
        var testo = string.Join(" · ", contenuto.Take(3).Select(v => string.Join(", ", v.Valori))).ReplaceLineEndings(" ");
        return testo.Length <= LunghezzaAnteprima ? testo : testo[..LunghezzaAnteprima] + "…";
    }

    private static void Riga(StringBuilder s, IEnumerable<string> campi)
    {
        s.AppendJoin(';', campi.Select(Racchiudi));
        s.Append("\r\n");
    }

    /// <summary>Testo libero: neutralizza l'inizio che un foglio di calcolo eseguirebbe come formula.</summary>
    internal static string Testo(string? valore)
    {
        if (string.IsNullOrEmpty(valore))
        {
            return string.Empty;
        }

        return valore[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n' ? "'" + valore : valore;
    }

    private static string Racchiudi(string valore) =>
        valore.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + valore.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : valore;

    private Task<Modulo?> TrovaModuloAsync(Guid idModulo, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return _contesto.Moduli.AsNoTracking().SingleOrDefaultAsync(m => m.Id == idModulo && m.IdSito == idSito, annullamento);
    }

    private async Task<InvioModulo?> TrovaInvioAsync(Guid idModulo, Guid idRichiesta, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        return await _contesto.InviiModulo.SingleOrDefaultAsync(i => i.Id == idRichiesta && i.IdModulo == idModulo && i.IdSito == idSito, annullamento);
    }

    private Task<Utenza?> TrovaAttoreAsync(Guid idAttore, CancellationToken annullamento)
    {
        var idTenant = _tenant.IdTenant;
        return _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && candidata.Ruolo == RuoloUtenza.TenantAdmin
                && candidata.IdTenant == idTenant,
            annullamento);
    }

    private async Task RegistraNegazioneAsync(Guid idAttore, string azione)
    {
        var attoreEsiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(candidata => candidata.Id == idAttore);
        await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "Modulo", RegistroAudit.EsitoNegato, _tenant.IdTenant, attoreEsiste ? idAttore : null, $"azione richiesta: {azione}", CancellationToken.None);
    }
}

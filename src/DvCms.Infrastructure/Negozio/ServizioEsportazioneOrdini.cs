using System.Globalization;
using System.Text;
using DvCms.Application.Accessi;
using DvCms.Application.Negozio;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>
/// Esportazione CSV degli ordini (vedi <see cref="IServizioEsportazioneOrdini"/>). Formato pensato per Excel in italiano:
/// UTF-8 con BOM, separatore <c>;</c>, decimali con la virgola, date UTC <c>aaaa-mm-gg hh:mm:ss</c>. I campi testuali che
/// iniziano con <c>= + - @</c> o con un carattere di controllo sono preceduti da un apice, perché un foglio di calcolo non li
/// esegua come formule (un acquirente sceglie liberamente nome, indirizzo e persino parte dell'email).
/// </summary>
public sealed class ServizioEsportazioneOrdini : IServizioEsportazioneOrdini
{
    private static readonly CultureInfo _cultura = CultureInfo.GetCultureInfo("it-IT");

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IServizioAudit _audit;
    private readonly TimeProvider _orologio;

    public ServizioEsportazioneOrdini(ContestoDatabase contesto, IContestoTenant tenant, IServizioAudit audit, TimeProvider orologio)
    {
        _contesto = contesto;
        _tenant = tenant;
        _audit = audit;
        _orologio = orologio;
    }

    public async Task<EsportazioneOrdini> EsportaAsync(Guid idAttore, FiltroEsportazioneOrdini filtro, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var idTenant = _tenant.IdTenant;
        var attore = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            candidata => candidata.Id == idAttore
                && candidata.Stato == StatoUtenza.Attiva
                && (candidata.Ruolo == RuoloUtenza.PlatformAdmin || (candidata.Ruolo == RuoloUtenza.TenantAdmin && candidata.IdTenant == idTenant)),
            annullamento);
        if (attore is null)
        {
            var esiste = await _contesto.Utenze.IgnoreQueryFilters().AnyAsync(c => c.Id == idAttore, annullamento);
            await _audit.RegistraAsync(AzioniAudit.OperazioneNegata, "Ordini", RegistroAudit.EsitoNegato, idTenant, esiste ? idAttore : null, $"azione richiesta: {AzioniAudit.EsportazioneOrdini}", CancellationToken.None);
            throw new UnauthorizedAccessException("Operazione riservata agli amministratori del tenant.");
        }

        if (!Enum.IsDefined(filtro.Formato) || (filtro.Stato is { } stato && !Enum.IsDefined(stato)))
        {
            throw new ArgumentException("Formato o stato non validi.", nameof(filtro));
        }

        var idSito = _tenant.IdSito;
        var ordini = _contesto.Ordini.AsNoTracking().Where(o => o.IdSito == idSito && o.IdTenant == idTenant);
        if (filtro.Dal is { } dal)
        {
            var inizio = dal.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            ordini = ordini.Where(o => o.CreatoIlUtc >= inizio);
        }

        if (filtro.Al is { } al)
        {
            var fine = al.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            ordini = ordini.Where(o => o.CreatoIlUtc < fine);
        }

        if (filtro.Stato is { } statoRichiesto)
        {
            ordini = ordini.Where(o => o.Stato == statoRichiesto);
        }

        var elenco = await ordini.OrderBy(o => o.Numero).Take(IServizioEsportazioneOrdini.MassimoOrdini + 1).ToListAsync(annullamento);
        var troncata = elenco.Count > IServizioEsportazioneOrdini.MassimoOrdini;
        if (troncata)
        {
            elenco.RemoveAt(elenco.Count - 1);
        }

        var scrittore = new StringBuilder();
        var righe = filtro.Formato == FormatoEsportazioneOrdini.Ordini
            ? ScriviOrdini(scrittore, elenco)
            : await ScriviRigheAsync(scrittore, elenco, idSito, idTenant, annullamento);

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        var nomeFile = string.Create(CultureInfo.InvariantCulture, $"{(filtro.Formato == FormatoEsportazioneOrdini.Ordini ? "ordini" : "righe-ordini")}-{adesso:yyyyMMdd-HHmmss}.csv");
        var contenuto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(scrittore.ToString())).ToArray();

        await _audit.RegistraAsync(
            AzioniAudit.EsportazioneOrdini,
            "Ordini",
            RegistroAudit.EsitoRiuscito,
            idTenant,
            attore.Id,
            string.Create(CultureInfo.InvariantCulture, $"formato {filtro.Formato}; righe {righe}; dal {filtro.Dal?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"} al {filtro.Al?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"}; stato {filtro.Stato?.ToString() ?? "-"}{(troncata ? "; troncata" : string.Empty)}"),
            annullamento);
        return new EsportazioneOrdini(nomeFile, contenuto, righe, troncata);
    }

    private static int ScriviOrdini(StringBuilder s, List<Ordine> ordini)
    {
        Riga(s, "Numero", "DataUtc", "Stato", "Valuta", "TotaleLordo", "TotaleImponibile", "TotaleIva", "Rimborsato", "Spedizione", "SpedizioneLorda", "Email", "Nome", "Destinatario", "Indirizzo1", "Indirizzo2", "Cap", "Citta", "Paese", "Pagamento", "CommissioneLorda");
        foreach (var o in ordini)
        {
            Riga(
                s,
                Num(o.Numero),
                Data(o.CreatoIlUtc),
                o.Stato.ToString(),
                Testo(o.Valuta),
                Dec(o.TotaleLordo),
                Dec(o.TotaleImponibile),
                Dec(o.TotaleIva),
                Dec(o.TotaleRimborsato),
                Testo(o.NomeSpedizione),
                Dec(o.SpedizioneLorda),
                Testo(o.EmailAcquirente),
                Testo(o.NomeAcquirente),
                Testo(o.Destinatario),
                Testo(o.IndirizzoRiga1),
                Testo(o.IndirizzoRiga2),
                Testo(o.Cap),
                Testo(o.Citta),
                Testo(o.Paese),
                o.ModalitaPagamento.ToString(),
                Dec(o.CommissioneLorda));
        }

        return ordini.Count;
    }

    private async Task<int> ScriviRigheAsync(StringBuilder s, List<Ordine> ordini, Guid idSito, Guid idTenant, CancellationToken annullamento)
    {
        Riga(s, "Numero", "DataUtc", "Stato", "Riga", "Sku", "Prodotto", "Variante", "Quantita", "PrezzoUnitarioLordo", "AliquotaIva", "Lordo", "Imponibile", "Iva", "Email");
        var righe = new Dictionary<Guid, List<RigaOrdine>>();
        // A blocchi, per non passare decine di migliaia di identificativi in una sola query.
        foreach (var blocco in ordini.Select(o => o.Id).Chunk(1000))
        {
            var lette = await _contesto.RigheOrdine.AsNoTracking()
                .Where(r => r.IdSito == idSito && r.IdTenant == idTenant && blocco.Contains(r.IdOrdine))
                .ToListAsync(annullamento);
            foreach (var gruppo in lette.GroupBy(r => r.IdOrdine))
            {
                righe[gruppo.Key] = [.. gruppo.OrderBy(r => r.Posizione)];
            }
        }

        var scritte = 0;
        foreach (var o in ordini)
        {
            foreach (var r in righe.GetValueOrDefault(o.Id) ?? [])
            {
                Riga(s, Num(o.Numero), Data(o.CreatoIlUtc), o.Stato.ToString(), Num(r.Posizione + 1), Testo(r.Sku), Testo(r.NomeProdotto), Testo(r.EtichettaVariante), Num(r.Quantita), Dec(r.PrezzoUnitarioLordo), Dec(r.AliquotaIva), Dec(r.Lordo), Dec(r.Imponibile), Dec(r.Iva), Testo(o.EmailAcquirente));
                scritte++;
            }

            if (o.SpedizioneLorda > 0)
            {
                Riga(s, Num(o.Numero), Data(o.CreatoIlUtc), o.Stato.ToString(), "Spedizione", string.Empty, Testo(o.NomeSpedizione), string.Empty, Num(1), Dec(o.SpedizioneLorda), string.Empty, Dec(o.SpedizioneLorda), Dec(o.SpedizioneImponibile), Dec(o.SpedizioneIva), Testo(o.EmailAcquirente));
                scritte++;
            }

            // La commissione del contrassegno è una riga a sé, così le righe sommano al totale dell'ordine.
            if (o.CommissioneLorda > 0)
            {
                Riga(s, Num(o.Numero), Data(o.CreatoIlUtc), o.Stato.ToString(), "Commissione", string.Empty, "Contrassegno", string.Empty, Num(1), Dec(o.CommissioneLorda), string.Empty, Dec(o.CommissioneLorda), Dec(o.CommissioneImponibile), Dec(o.CommissioneIva), Testo(o.EmailAcquirente));
                scritte++;
            }
        }

        return scritte;
    }

    private static void Riga(StringBuilder s, params string[] campi)
    {
        for (var i = 0; i < campi.Length; i++)
        {
            if (i > 0)
            {
                s.Append(';');
            }

            s.Append(Racchiudi(campi[i]));
        }

        s.Append("\r\n");
    }

    private static string Num(int valore) => valore.ToString(CultureInfo.InvariantCulture);

    private static string Dec(decimal valore) => valore.ToString("0.00", _cultura);

    private static string Data(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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
}

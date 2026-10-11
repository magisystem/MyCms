using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>
/// Operazioni sulla giacenza da eseguire dentro la transazione di chi le chiama (creazione, annullamento, pagamento, scadenza di un
/// ordine). Ogni variazione di quantità è un'unica istruzione condizionale: niente lettura seguita da scrittura, quindi nessuna
/// finestra in cui due richieste vedano la stessa disponibilità.
/// </summary>
internal static class OperazioniGiacenza
{
    public sealed record EsitoPrenotazione(IReadOnlyList<Guid> Insufficienti, int Prenotate);

    /// <summary>
    /// Trattiene le quantità delle righe che hanno giacenza gestita. Le varianti senza giacenza si saltano. Se anche una sola non basta
    /// restituisce l'elenco di quelle che mancano e il chiamante deve annullare la transazione.
    /// </summary>
    public static async Task<EsitoPrenotazione> PrenotaAsync(
        ContestoDatabase db,
        Ordine ordine,
        IEnumerable<(Guid IdVariante, int Quantita)> righe,
        DateTime adesso,
        DateTime scade,
        Func<Guid> nuovoId,
        CancellationToken annullamento)
    {
        var insufficienti = new List<Guid>();
        var prenotate = 0;

        // Ordine fisso delle varianti: due ordini che si contendono le stesse non si bloccano a vicenda.
        foreach (var (idVariante, quantita) in righe.OrderBy(r => r.IdVariante))
        {
            if (!await db.GiacenzeVarianti.IgnoreQueryFilters().AnyAsync(g => g.IdVariante == idVariante && g.IdSito == ordine.IdSito && g.IdTenant == ordine.IdTenant, annullamento))
            {
                continue;
            }

            var q = quantita;
            var riuscita = await db.GiacenzeVarianti.IgnoreQueryFilters()
                .Where(g => g.IdVariante == idVariante && g.IdSito == ordine.IdSito && g.IdTenant == ordine.IdTenant && g.Disponibile - g.Riservata >= q)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.Riservata, g => g.Riservata + q).SetProperty(g => g.AggiornataIlUtc, adesso), annullamento);
            if (riuscita == 0)
            {
                insufficienti.Add(idVariante);
                continue;
            }

            prenotate++;
            db.PrenotazioniGiacenza.Add(new PrenotazioneGiacenza
            {
                Id = nuovoId(),
                IdTenant = ordine.IdTenant,
                IdSito = ordine.IdSito,
                IdOrdine = ordine.Id,
                IdVariante = idVariante,
                Quantita = q,
                Stato = StatoPrenotazione.Attiva,
                ScadeIlUtc = scade,
                AggiornataIlUtc = adesso,
            });
        }

        return new EsitoPrenotazione(insufficienti, prenotate);
    }

    /// <summary>Rilascia le prenotazioni attive dell'ordine. Ogni prenotazione si chiude con un'istruzione condizionale: due rilasci insieme non restituiscono due volte.</summary>
    public static async Task<int> RilasciaAsync(ContestoDatabase db, Guid idTenant, Guid idOrdine, DateTime adesso, CancellationToken annullamento)
    {
        var attive = await db.PrenotazioniGiacenza.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IdTenant == idTenant && p.IdOrdine == idOrdine && p.Stato == StatoPrenotazione.Attiva)
            .Select(p => new { p.Id, p.IdVariante, p.Quantita })
            .ToListAsync(annullamento);

        var rilasciate = 0;
        foreach (var p in attive.OrderBy(x => x.IdVariante))
        {
            var chiusa = await db.PrenotazioniGiacenza.IgnoreQueryFilters()
                .Where(x => x.Id == p.Id && x.Stato == StatoPrenotazione.Attiva)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Stato, StatoPrenotazione.Rilasciata).SetProperty(x => x.AggiornataIlUtc, adesso), annullamento);
            if (chiusa == 0)
            {
                continue;
            }

            var q = p.Quantita;
            await db.GiacenzeVarianti.IgnoreQueryFilters()
                .Where(g => g.IdVariante == p.IdVariante && g.IdTenant == idTenant)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.Riservata, g => g.Riservata - q).SetProperty(g => g.AggiornataIlUtc, adesso), annullamento);
            rilasciate++;
        }

        return rilasciate;
    }

    /// <summary>
    /// Conferma le prenotazioni attive: la quantità esce dal magazzino (<c>Disponibile</c> e <c>Riservata</c> scendono insieme) e resta
    /// un movimento di vendita. Falso se una prenotazione dell'ordine era già stata rilasciata: la merce non è più garantita.
    /// </summary>
    public static async Task<bool> ConfermaAsync(ContestoDatabase db, Guid idTenant, Guid idSito, Guid idOrdine, DateTime adesso, Func<Guid> nuovoId, CancellationToken annullamento)
    {
        var prenotazioni = await db.PrenotazioniGiacenza.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IdTenant == idTenant && p.IdOrdine == idOrdine)
            .Select(p => new { p.Id, p.IdVariante, p.Quantita, p.Stato })
            .ToListAsync(annullamento);
        if (prenotazioni.Any(p => p.Stato == StatoPrenotazione.Rilasciata))
        {
            return false;
        }

        foreach (var p in prenotazioni.Where(x => x.Stato == StatoPrenotazione.Attiva).OrderBy(x => x.IdVariante))
        {
            var chiusa = await db.PrenotazioniGiacenza.IgnoreQueryFilters()
                .Where(x => x.Id == p.Id && x.Stato == StatoPrenotazione.Attiva)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Stato, StatoPrenotazione.Confermata).SetProperty(x => x.AggiornataIlUtc, adesso), annullamento);
            if (chiusa == 0)
            {
                continue;
            }

            var q = p.Quantita;
            await db.GiacenzeVarianti.IgnoreQueryFilters()
                .Where(g => g.IdVariante == p.IdVariante && g.IdTenant == idTenant)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(g => g.Disponibile, g => g.Disponibile - q).SetProperty(g => g.Riservata, g => g.Riservata - q).SetProperty(g => g.AggiornataIlUtc, adesso),
                    annullamento);
            var dopo = await db.GiacenzeVarianti.IgnoreQueryFilters().AsNoTracking().Where(g => g.IdVariante == p.IdVariante).Select(g => g.Disponibile).SingleAsync(annullamento);
            db.MovimentiGiacenza.Add(new MovimentoGiacenza
            {
                Id = nuovoId(),
                IdTenant = idTenant,
                IdSito = idSito,
                IdVariante = p.IdVariante,
                Motivo = MotivoMovimento.Vendita,
                Variazione = -q,
                DisponibileDopo = dopo,
                IdOrdine = idOrdine,
                CreatoIlUtc = adesso,
            });
        }

        return true;
    }
}

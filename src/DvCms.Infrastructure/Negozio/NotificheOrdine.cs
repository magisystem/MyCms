using System.Globalization;
using System.Text;
using DvCms.Application.Email;
using DvCms.Application.Localizzazione;
using DvCms.Application.Tenancy;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>
/// Email dell'ordine al cliente. Si accoda nell'outbox **nella stessa transazione** che segna l'ordine pagato (quindi o ci sono
/// entrambi o nessuno dei due), cifrata anche a riposo perché contiene l'indirizzo di consegna. Il testo è nella lingua in cui il
/// cliente ha acquistato. Se il tenant non ha un canale email configurato non si accoda nulla: l'ordine non ne dipende.
/// </summary>
public sealed class NotificheOrdine
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IAccodatoreEmail _email;
    private readonly ILocalizzatore _localizzatore;

    public NotificheOrdine(ContestoDatabase contesto, IContestoTenant tenant, IAccodatoreEmail email, ILocalizzatore localizzatore)
    {
        _contesto = contesto;
        _tenant = tenant;
        _email = email;
        _localizzatore = localizzatore;
    }

    /// <summary>Accoda la conferma di un ordine appena pagato. Non salva: lo fa chi chiama, nella sua transazione. Vero se l'ha accodata.</summary>
    public async Task<bool> AccodaConfermaPagamentoAsync(Ordine ordine, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(ordine);

        var idTenant = _tenant.IdTenant;
        if (!await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.IdTenant == idTenant, annullamento))
        {
            return false;
        }

        var righe = await _contesto.RigheOrdine.AsNoTracking().Where(r => r.IdOrdine == ordine.Id).OrderBy(r => r.Posizione).ToListAsync(annullamento);
        var nomeSito = await _contesto.Siti.AsNoTracking().Where(s => s.Id == ordine.IdSito).Select(s => s.Nome).SingleAsync(annullamento);
        var lingua = ordine.CodiceLingua;
        var cultura = Cultura(lingua);
        string Importo(decimal valore) => $"{valore.ToString("N2", cultura)} {ordine.Valuta}";

        var testo = new StringBuilder();
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Grazie per il tuo ordine n. {0} su {1}: abbiamo ricevuto il pagamento.", ordine.Numero, nomeSito));
        testo.AppendLine();
        foreach (var riga in righe)
        {
            var nome = string.IsNullOrEmpty(riga.EtichettaVariante) ? riga.NomeProdotto : $"{riga.NomeProdotto} — {riga.EtichettaVariante}";
            testo.AppendLine(_localizzatore.TraduciIn(lingua, "- {0} (codice {1}) × {2}: {3}", nome, riga.Sku, riga.Quantita, Importo(riga.Lordo)));
        }

        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Consegna ({0}): {1}", ordine.NomeSpedizione, Importo(ordine.SpedizioneLorda)));
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Totale (IVA inclusa): {0}", Importo(ordine.TotaleLordo)));
        testo.AppendLine();
        if (ordine.TipoSpedizione == TipoMetodoSpedizione.Ritiro)
        {
            testo.AppendLine(_localizzatore.TraduciIn(lingua, "Ritiro in sede."));
        }
        else
        {
            testo.AppendLine(_localizzatore.TraduciIn(lingua, "Consegna a:"));
            testo.AppendLine(ordine.Destinatario);
            testo.AppendLine(ordine.IndirizzoRiga1);
            if (!string.IsNullOrEmpty(ordine.IndirizzoRiga2))
            {
                testo.AppendLine(ordine.IndirizzoRiga2);
            }

            testo.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{ordine.Cap} {ordine.Citta} ({ordine.Paese})"));
        }

        testo.AppendLine();
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Puoi consultare l'ordine qui: {0}", $"https://{_tenant.Host}/account/ordini/{ordine.Id:D}"));

        _email.Accoda(
            idTenant,
            new MessaggioEmail(
                ordine.EmailAcquirente,
                ordine.NomeAcquirente,
                _localizzatore.TraduciIn(lingua, "Ordine n. {0} confermato - {1}", ordine.Numero, nomeSito),
                testo.ToString()),
            chiaveIdempotenza: $"ordine-pagato:{ordine.Id:D}",
            riservato: true);
        return true;
    }

    /// <summary>
    /// Accoda l'avviso di un nuovo ordine pagato agli amministratori attivi del tenant (al più dieci), nella lingua predefinita del sito.
    /// Una email per amministratore, con chiave di idempotenza propria. Non salva: lo fa chi chiama, nella sua transazione.
    /// </summary>
    public async Task<int> AccodaAvvisoAmministratoriAsync(Ordine ordine, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(ordine);

        var idTenant = _tenant.IdTenant;
        if (!await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.IdTenant == idTenant, annullamento))
        {
            return 0;
        }

        var destinatari = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IdTenant == idTenant && u.Ruolo == DvCms.Domain.Accessi.RuoloUtenza.TenantAdmin && u.Stato == DvCms.Domain.Accessi.StatoUtenza.Attiva)
            .OrderBy(u => u.CreatoIlUtc)
            .Select(u => new { u.Id, u.Email, u.NomeVisualizzato })
            .Take(10)
            .ToListAsync(annullamento);
        if (destinatari.Count == 0)
        {
            return 0;
        }

        var lingua = (await DvCms.Infrastructure.Cms.LingueDelSito.ElencaAsync(_contesto, ordine.IdSito, annullamento)).FirstOrDefault(l => l.Predefinita)?.Codice ?? "it";
        var nomeSito = await _contesto.Siti.AsNoTracking().Where(s => s.Id == ordine.IdSito).Select(s => s.Nome).SingleAsync(annullamento);
        var articoli = await _contesto.RigheOrdine.AsNoTracking().Where(r => r.IdOrdine == ordine.Id).SumAsync(r => r.Quantita, annullamento);
        var importo = $"{ordine.TotaleLordo.ToString("N2", Cultura(lingua))} {ordine.Valuta}";

        var testo = new StringBuilder();
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Nuovo ordine pagato n. {0} su {1}: {2}, {3} articoli.", ordine.Numero, nomeSito, importo, articoli));
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Cliente: {0} ({1})", ordine.NomeAcquirente, ordine.EmailAcquirente));
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Consegna: {0}", ordine.NomeSpedizione));
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Gestisci l'ordine qui: {0}", $"https://{_tenant.Host}/admin/negozio/ordini/{ordine.Id:D}"));

        foreach (var destinatario in destinatari)
        {
            _email.Accoda(
                idTenant,
                new MessaggioEmail(destinatario.Email, destinatario.NomeVisualizzato, _localizzatore.TraduciIn(lingua, "Nuovo ordine n. {0} - {1}", ordine.Numero, nomeSito), testo.ToString()),
                chiaveIdempotenza: $"ordine-pagato-admin:{ordine.Id:D}:{destinatario.Id:D}",
                riservato: true);
        }

        return destinatari.Count;
    }

    /// <summary>Accoda l'avviso di un rimborso concluso. Non salva: lo fa chi chiama, nella sua transazione. Vero se l'ha accodato.</summary>
    public async Task<bool> AccodaRimborsoAsync(Ordine ordine, Rimborso rimborso, bool totale, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(ordine);
        ArgumentNullException.ThrowIfNull(rimborso);

        var idTenant = _tenant.IdTenant;
        if (!await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.IdTenant == idTenant, annullamento))
        {
            return false;
        }

        var nomeSito = await _contesto.Siti.AsNoTracking().Where(s => s.Id == ordine.IdSito).Select(s => s.Nome).SingleAsync(annullamento);
        var lingua = ordine.CodiceLingua;
        var importo = $"{rimborso.Importo.ToString("N2", Cultura(lingua))} {rimborso.Valuta}";
        var testo = new StringBuilder();
        if (totale)
        {
            testo.AppendLine(_localizzatore.TraduciIn(lingua, "Abbiamo rimborsato per intero l'ordine n. {0} su {1}: {2}.", ordine.Numero, nomeSito, importo));
        }
        else
        {
            testo.AppendLine(_localizzatore.TraduciIn(lingua, "Abbiamo rimborsato una parte dell'ordine n. {0} su {1}: {2}.", ordine.Numero, nomeSito, importo));
        }

        if (!string.IsNullOrEmpty(rimborso.Motivo))
        {
            testo.AppendLine(_localizzatore.TraduciIn(lingua, "Motivo: {0}", rimborso.Motivo));
        }

        testo.AppendLine(_localizzatore.TraduciIn(lingua, "L'accredito sul metodo di pagamento che hai usato può richiedere alcuni giorni."));
        testo.AppendLine(_localizzatore.TraduciIn(lingua, "Puoi consultare l'ordine qui: {0}", $"https://{_tenant.Host}/account/ordini/{ordine.Id:D}"));
        _email.Accoda(
            idTenant,
            new MessaggioEmail(ordine.EmailAcquirente, ordine.NomeAcquirente, _localizzatore.TraduciIn(lingua, "Rimborso per l'ordine n. {0} - {1}", ordine.Numero, nomeSito), testo.ToString()),
            chiaveIdempotenza: $"rimborso:{rimborso.Id:D}",
            riservato: true);
        return true;
    }

    private static CultureInfo Cultura(string codiceLingua)
    {
        try
        {
            return CultureInfo.GetCultureInfo(codiceLingua);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

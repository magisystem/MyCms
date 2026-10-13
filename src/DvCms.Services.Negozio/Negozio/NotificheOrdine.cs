using System.Globalization;
using DvCms.Contracts.Email;
using DvCms.Contracts.Tenancy;
using DvCms.Data;
using DvCms.Data.Cms;
using DvCms.DTO.Email;
using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Negozio;

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
    private readonly IComponitoreEmail _modelli;

    public NotificheOrdine(ContestoDatabase contesto, IContestoTenant tenant, IAccodatoreEmail email, IComponitoreEmail modelli)
    {
        _contesto = contesto;
        _tenant = tenant;
        _email = email;
        _modelli = modelli;
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

        var messaggio = await _modelli.ComponiAsync(
            idTenant,
            CodiciModelloEmail.OrdineConfermato,
            lingua,
            ordine.EmailAcquirente,
            ordine.NomeAcquirente,
            Dettaglio(ordine, righe, nomeSito, Importo),
            annullamento);
        _email.Accoda(
            idTenant,
            messaggio,
            chiaveIdempotenza: $"ordine-pagato:{ordine.Id:D}",
            riservato: true);
        return true;
    }

    /// <summary>
    /// Accoda al cliente la ricezione di un ordine con pagamento manuale (ADR-0023): per il bonifico le coordinate, l'importo, la causale e il termine;
    /// per il contrassegno l'importo da pagare alla consegna. Non salva: lo fa chi chiama, nella sua transazione. Vero se l'ha accodata.
    /// </summary>
    public async Task<bool> AccodaOrdineRicevutoAsync(Ordine ordine, ImpostazioniPagamentoOffline impostazioni, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(ordine);
        ArgumentNullException.ThrowIfNull(impostazioni);

        var idTenant = _tenant.IdTenant;
        if (ordine.ModalitaPagamento == ModalitaPagamento.Online
            || !await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.IdTenant == idTenant, annullamento))
        {
            return false;
        }

        var righe = await _contesto.RigheOrdine.AsNoTracking().Where(r => r.IdOrdine == ordine.Id).OrderBy(r => r.Posizione).ToListAsync(annullamento);
        var nomeSito = await _contesto.Siti.AsNoTracking().Where(s => s.Id == ordine.IdSito).Select(s => s.Nome).SingleAsync(annullamento);
        var lingua = ordine.CodiceLingua;
        var cultura = Cultura(lingua);
        string Importo(decimal valore) => $"{valore.ToString("N2", cultura)} {ordine.Valuta}";

        var variabili = Dettaglio(ordine, righe, nomeSito, Importo);
        variabili["importo"] = Importo(ordine.TotaleLordo);
        variabili["istruzioni"] = ordine.ModalitaPagamento == ModalitaPagamento.Bonifico ? Vuoto(impostazioni.IstruzioniBonifico) : Vuoto(impostazioni.IstruzioniContrassegno);
        if (ordine.ModalitaPagamento == ModalitaPagamento.Bonifico)
        {
            variabili["intestatario"] = impostazioni.Intestatario ?? string.Empty;
            variabili["iban"] = impostazioni.Iban is null ? string.Empty : Iban.Formatta(impostazioni.Iban);
            variabili["bic"] = Vuoto(impostazioni.Bic);
            variabili["banca"] = Vuoto(impostazioni.NomeBanca);
            variabili["causale"] = CausaleBonifico.Per(ordine.Numero);
            variabili["scadenza"] = ordine.PrenotazioneScadeIlUtc is { } scadenza ? scadenza.ToString("d", cultura) : null;
        }

        var messaggio = await _modelli.ComponiAsync(
            idTenant,
            ordine.ModalitaPagamento == ModalitaPagamento.Bonifico ? CodiciModelloEmail.OrdineRicevutoBonifico : CodiciModelloEmail.OrdineRicevutoContrassegno,
            lingua,
            ordine.EmailAcquirente,
            ordine.NomeAcquirente,
            variabili,
            annullamento);
        _email.Accoda(
            idTenant,
            messaggio,
            chiaveIdempotenza: $"ordine-ricevuto:{ordine.Id:D}",
            riservato: true);
        return true;
    }

    /// <summary>Le variabili comuni alle email dell'ordine: righe, consegna, totali e indirizzo (importi già formattati nella lingua del cliente).</summary>
    private Dictionary<string, object?> Dettaglio(Ordine ordine, IReadOnlyList<RigaOrdine> righe, string nomeSito, Func<decimal, string> importo)
    {
        var indirizzo = new List<object?>();
        if (ordine.TipoSpedizione != TipoMetodoSpedizione.Ritiro)
        {
            indirizzo.Add(ordine.Destinatario);
            indirizzo.Add(ordine.IndirizzoRiga1);
            if (!string.IsNullOrEmpty(ordine.IndirizzoRiga2))
            {
                indirizzo.Add(ordine.IndirizzoRiga2);
            }

            indirizzo.Add(string.Create(CultureInfo.InvariantCulture, $"{ordine.Cap} {ordine.Citta} ({ordine.Paese})"));
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["numero"] = ordine.Numero,
            ["nome_sito"] = nomeSito,
            ["righe"] = righe.Select(riga => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["nome"] = string.IsNullOrEmpty(riga.EtichettaVariante) ? riga.NomeProdotto : $"{riga.NomeProdotto} — {riga.EtichettaVariante}",
                ["codice"] = riga.Sku,
                ["quantita"] = riga.Quantita,
                ["importo"] = importo(riga.Lordo),
            }).ToList(),
            ["consegna_nome"] = ordine.NomeSpedizione,
            ["consegna_importo"] = importo(ordine.SpedizioneLorda),
            ["commissione"] = ordine.CommissioneLorda > 0 ? importo(ordine.CommissioneLorda) : null,
            ["totale"] = importo(ordine.TotaleLordo),
            ["ritiro"] = ordine.TipoSpedizione == TipoMetodoSpedizione.Ritiro,
            ["indirizzo"] = indirizzo,
            ["link_ordine"] = $"https://{_tenant.Host}/account/ordini/{ordine.Id:D}",
        };
    }

    /// <summary>Un testo opzionale per i modelli: vuoto diventa «assente», così <c>{% if %}</c> lo salta.</summary>
    private static string? Vuoto(string? testo) => string.IsNullOrEmpty(testo) ? null : testo;

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
            .Where(u => u.IdTenant == idTenant && u.Ruolo == DvCms.Models.Accessi.RuoloUtenza.TenantAdmin && u.Stato == DvCms.Models.Accessi.StatoUtenza.Attiva)
            .OrderBy(u => u.CreatoIlUtc)
            .Select(u => new { u.Id, u.Email, u.NomeVisualizzato })
            .Take(10)
            .ToListAsync(annullamento);
        if (destinatari.Count == 0)
        {
            return 0;
        }

        var lingua = (await DvCms.Data.Cms.LingueDelSito.ElencaAsync(_contesto, ordine.IdSito, annullamento)).FirstOrDefault(l => l.Predefinita)?.Codice ?? "it";
        var nomeSito = await _contesto.Siti.AsNoTracking().Where(s => s.Id == ordine.IdSito).Select(s => s.Nome).SingleAsync(annullamento);
        var articoli = await _contesto.RigheOrdine.AsNoTracking().Where(r => r.IdOrdine == ordine.Id).SumAsync(r => r.Quantita, annullamento);
        var importo = $"{ordine.TotaleLordo.ToString("N2", Cultura(lingua))} {ordine.Valuta}";

        var tipo = ordine.ModalitaPagamento switch
        {
            ModalitaPagamento.Bonifico => "bonifico",
            ModalitaPagamento.Contrassegno => "contrassegno",
            _ => "pagato",
        };
        var variabili = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["numero"] = ordine.Numero,
            ["nome_sito"] = nomeSito,
            ["tipo"] = tipo,
            ["importo"] = importo,
            ["articoli"] = articoli,
            ["cliente_nome"] = ordine.NomeAcquirente,
            ["cliente_email"] = ordine.EmailAcquirente,
            ["consegna_nome"] = ordine.NomeSpedizione,
            ["link_ordine"] = $"https://{_tenant.Host}/admin/negozio/ordini/{ordine.Id:D}",
        };

        foreach (var destinatario in destinatari)
        {
            variabili["nome"] = destinatario.NomeVisualizzato;
            var messaggio = await _modelli.ComponiAsync(idTenant, CodiciModelloEmail.OrdineNuovoAmministratore, lingua, destinatario.Email, destinatario.NomeVisualizzato, variabili, annullamento);
            _email.Accoda(
                idTenant,
                messaggio,
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
        var messaggio = await _modelli.ComponiAsync(
            idTenant,
            CodiciModelloEmail.Rimborso,
            lingua,
            ordine.EmailAcquirente,
            ordine.NomeAcquirente,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["numero"] = ordine.Numero,
                ["nome_sito"] = nomeSito,
                ["importo"] = importo,
                ["totale"] = totale,
                ["motivo"] = Vuoto(rimborso.Motivo),
                ["link_ordine"] = $"https://{_tenant.Host}/account/ordini/{ordine.Id:D}",
            },
            annullamento);
        _email.Accoda(
            idTenant,
            messaggio,
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

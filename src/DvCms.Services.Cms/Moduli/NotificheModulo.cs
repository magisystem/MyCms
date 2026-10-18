using DvCms.Contracts.Email;
using DvCms.Contracts.Tenancy;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.DTO.Moduli;
using DvCms.Models.Accessi;
using DvCms.Models.Moduli;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Services.Cms.Moduli;

/// <summary>
/// Email di una richiesta ricevuta da un modulo (ADR-0027, §5). Contengono i dati personali del visitatore, quindi si accodano come
/// riservate (cifrate anche nell'outbox) e **nella stessa transazione** che salva la richiesta: o ci sono entrambe o nessuna. Se il tenant
/// non ha un canale email non si accoda nulla e la richiesta resta comunque consultabile nell'admin.
/// </summary>
public sealed class NotificheModulo
{
    /// <summary>Tetto agli amministratori avvisati quando il modulo ha scelto «tutti i TenantAdmin».</summary>
    public const int AmministratoriMassimi = 10;

    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IAccodatoreEmail _email;
    private readonly IComponitoreEmail _modelli;

    public NotificheModulo(ContestoDatabase contesto, IContestoTenant tenant, IAccodatoreEmail email, IComponitoreEmail modelli)
    {
        _contesto = contesto;
        _tenant = tenant;
        _email = email;
        _modelli = modelli;
    }

    /// <summary>
    /// Accoda l'avviso ai destinatari del modulo e, se il modulo lo prevede e il visitatore ha lasciato un'email, la risposta automatica.
    /// Non salva: lo fa chi chiama. Restituisce il numero di email accodate.
    /// </summary>
    public async Task<int> AccodaAsync(Modulo modulo, ImpostazioniModulo impostazioni, InvioModulo invio, ModuloPubblico pubblico, IReadOnlyList<ValoreInviato> contenuto, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(modulo);
        ArgumentNullException.ThrowIfNull(impostazioni);
        ArgumentNullException.ThrowIfNull(invio);
        ArgumentNullException.ThrowIfNull(pubblico);
        ArgumentNullException.ThrowIfNull(contenuto);

        var idTenant = _tenant.IdTenant;
        if (!await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.IdTenant == idTenant, annullamento))
        {
            return 0;
        }

        var nomeSito = await _contesto.Siti.AsNoTracking().Where(s => s.Id == modulo.IdSito).Select(s => s.Nome).SingleAsync(annullamento);
        var campi = contenuto
            .Select(v => (object?)new Dictionary<string, object?>(StringComparer.Ordinal) { ["etichetta"] = v.Etichetta, ["valore"] = string.Join(", ", v.Valori) })
            .ToList();
        var accodate = 0;

        foreach (var destinatario in await DestinatariAsync(impostazioni, idTenant, annullamento))
        {
            var variabili = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["nome"] = destinatario.Nome,
                ["nome_modulo"] = modulo.Nome,
                ["nome_sito"] = nomeSito,
                ["lingua"] = pubblico.Lingua,
                ["campi"] = campi,
                ["link_richiesta"] = $"https://{_tenant.Host}/admin/moduli/richieste/{modulo.Id:D}/{invio.Id:D}",
            };
            var messaggio = await _modelli.ComponiAsync(idTenant, CodiciModelloEmail.NuovoInvioModulo, pubblico.LinguaPredefinita, destinatario.Email, destinatario.Nome, variabili, annullamento);
            _email.Accoda(idTenant, messaggio, chiaveIdempotenza: $"modulo-invio:{invio.Id:D}:{destinatario.Chiave}", riservato: true);
            accodate++;
        }

        if (impostazioni.RispostaAutomatica && EmailDelVisitatore(contenuto) is { } emailVisitatore)
        {
            var variabili = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["nome_modulo"] = modulo.Nome,
                ["nome_sito"] = nomeSito,
                ["messaggio"] = TestoConferma(pubblico),
                ["campi"] = campi,
            };
            var messaggio = await _modelli.ComponiAsync(idTenant, CodiciModelloEmail.RispostaInvioModulo, pubblico.Lingua, emailVisitatore, null, variabili, annullamento);
            _email.Accoda(idTenant, messaggio, chiaveIdempotenza: $"modulo-risposta:{invio.Id:D}", riservato: true);
            accodate++;
        }

        return accodate;
    }

    private static string TestoConferma(ModuloPubblico pubblico) =>
        DvCms.Core.Moduli.TestiModulo.Per(pubblico.Definizione.Conferma, pubblico.Lingua, pubblico.LinguaPredefinita);

    /// <summary>L'indirizzo scritto nel primo campo di tipo email compilato, già normalizzato dalla validazione.</summary>
    private static string? EmailDelVisitatore(IReadOnlyList<ValoreInviato> contenuto) =>
        contenuto.FirstOrDefault(v => v.Tipo == TipoCampoModulo.Email && v.Valori.Count == 1)?.Valori[0];

    private async Task<List<Destinatario>> DestinatariAsync(ImpostazioniModulo impostazioni, Guid idTenant, CancellationToken annullamento)
    {
        if (!impostazioni.TuttiGliAmministratori)
        {
            return [.. impostazioni.Destinatari
                .Where(indirizzo => !string.IsNullOrWhiteSpace(indirizzo))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(ImpostazioniModulo.NumeroMassimoDestinatari)
                .Select(indirizzo => new Destinatario(indirizzo, null, indirizzo.ToLowerInvariant()))];
        }

        var amministratori = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IdTenant == idTenant && u.Ruolo == RuoloUtenza.TenantAdmin && u.Stato == StatoUtenza.Attiva)
            .OrderBy(u => u.CreatoIlUtc)
            .Select(u => new { u.Id, u.Email, u.NomeVisualizzato })
            .Take(AmministratoriMassimi)
            .ToListAsync(annullamento);
        return [.. amministratori.Select(u => new Destinatario(u.Email, u.NomeVisualizzato, u.Id.ToString("D")))];
    }

    private sealed record Destinatario(string Email, string? Nome, string Chiave);
}

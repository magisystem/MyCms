using DvCms.Contracts.Negozio;
using DvCms.Data;
using DvCms.DTO.Manutenzione;
using DvCms.DTO.Negozio;
using DvCms.Infrastructure.Manutenzione;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models.Accessi;
using DvCms.Models.Cms;
using DvCms.Models.Email;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Pagamenti;

/// <summary>Verifica di prontezza del negozio (vedi <see cref="IServizioProntezzaNegozio"/>).</summary>
public sealed class ServizioProntezzaNegozio : IServizioProntezzaNegozio
{
    private readonly ContestoDatabase _contesto;
    private readonly IArchivioCredenzialiPagamento _credenziali;
    private readonly ServizioImpostazioniNexi _credenzialiNexi;
    private readonly OpzioniNegozio _opzioni;
    private readonly OpzioniManutenzione _manutenzione;

    public ServizioProntezzaNegozio(ContestoDatabase contesto, IArchivioCredenzialiPagamento credenziali, ServizioImpostazioniNexi credenzialiNexi, OpzioniNegozio opzioni, OpzioniManutenzione manutenzione)
    {
        _contesto = contesto;
        _credenziali = credenziali;
        _credenzialiNexi = credenzialiNexi;
        _opzioni = opzioni;
        _manutenzione = manutenzione;
    }

    public async Task<RapportoProntezza?> VerificaAsync(Guid idAttore, Guid idTenant, Guid idSito, CancellationToken annullamento)
    {
        if (!await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                u => u.Id == idAttore && u.Ruolo == RuoloUtenza.PlatformAdmin && u.Stato == StatoUtenza.Attiva,
                annullamento))
        {
            throw new UnauthorizedAccessException("Operazione riservata al PlatformAdmin.");
        }

        var sito = await _contesto.Siti.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.Id == idSito && s.IdTenant == idTenant, annullamento);
        if (sito is null)
        {
            return null;
        }

        var controlli = new List<ControlloProntezza>();
        void Aggiungi(string codice, bool ok, StatoControllo seManca, string? dettaglio = null) =>
            controlli.Add(new ControlloProntezza(codice, ok ? StatoControllo.Ok : seManca, dettaglio));

        Aggiungi("NegozioAttivo", sito.CommercioAttivo, StatoControllo.Bloccante);

        var impostazioni = await _contesto.ImpostazioniPagamentoSiti.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
        var credenziali = await _credenziali.OttieniAsync(idTenant, idSito, annullamento);
        var credenzialiNexi = await _credenzialiNexi.OttieniAsync(idTenant, idSito, annullamento);

        // Basta un metodo di pagamento pronto: quello non ancora configurato è solo un'attenzione, a meno che non ce ne sia nessuno.
        // I pagamenti manuali (bonifico, contrassegno; ADR-0023) contano come metodi pronti se attivi e completi.
        var offline = await _contesto.ImpostazioniPagamentoOfflineSiti.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.IdSito == idSito && i.IdTenant == idTenant, annullamento);
        var bonificoPronto = offline is { BonificoAttivo: true, Intestatario: { Length: > 0 }, Iban: { Length: > 0 } };
        var contrassegnoPronto = offline is { ContrassegnoAttivo: true };
        var manualePronto = bonificoPronto || contrassegnoPronto;
        var mancaPayPal = credenzialiNexi is null && !manualePronto ? StatoControllo.Bloccante : StatoControllo.Attenzione;
        var ambientePayPal = impostazioni?.Ambiente ?? AmbientePagamento.Sandbox;
        Aggiungi("CredenzialiPayPal", credenziali is not null, mancaPayPal, ambientePayPal.ToString());

        // L'identificativo del webhook fa parte delle credenziali complete: se si leggono, c'è. Senza, i pagamenti non si concludono da soli.
        Aggiungi("WebhookPayPal", credenziali is not null && !string.IsNullOrEmpty(credenziali.IdWebhook), mancaPayPal);
        if (credenzialiNexi is not null || credenziali is null)
        {
            Aggiungi("CredenzialiNexi", credenzialiNexi is not null, StatoControllo.Attenzione, credenzialiNexi?.Ambiente.ToString());
        }

        if (offline is { BonificoAttivo: true } && !bonificoPronto)
        {
            controlli.Add(new ControlloProntezza("BonificoIncompleto", StatoControllo.Bloccante));
        }

        if (manualePronto)
        {
            var attivi = string.Join(", ", new[] { bonificoPronto ? "bonifico" : null, contrassegnoPronto ? "contrassegno" : null }.Where(n => n is not null));
            controlli.Add(new ControlloProntezza("PagamentiManuali", StatoControllo.Ok, attivi));
        }

        // L'ambiente che conta è quello dei provider online configurati: se uno è in produzione serve l'interruttore. Senza provider online
        // (solo pagamenti manuali) non c'è ambiente di prova né interruttore.
        var ambiente = impostazioni is not null ? ambientePayPal : credenzialiNexi?.Ambiente ?? AmbientePagamento.Sandbox;
        var inProduzione = impostazioni?.Ambiente == AmbientePagamento.Live || credenzialiNexi?.Ambiente == AmbientePagamento.Live;
        var haProviderOnline = credenziali is not null || credenzialiNexi is not null;
        if (inProduzione)
        {
            Aggiungi("InterruttoreProduzione", _opzioni.CheckoutProduzioneAbilitato, StatoControllo.Bloccante);
        }
        else if (haProviderOnline || !manualePronto)
        {
            controlli.Add(new ControlloProntezza("AmbienteProva", StatoControllo.Attenzione, ambiente.ToString()));
        }

        var prodotti = await _contesto.Prodotti.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IdSito == idSito && p.IdTenant == idTenant && p.Stato == StatoProdotto.Pubblicato
                && _contesto.VariantiProdotto.IgnoreQueryFilters().Any(v => v.IdProdotto == p.Id && v.Attiva))
            .CountAsync(annullamento);
        Aggiungi("ProdottiPubblicati", prodotti > 0, StatoControllo.Bloccante, prodotti.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var spedizioni = await _contesto.MetodiSpedizione.IgnoreQueryFilters().AsNoTracking().CountAsync(m => m.IdSito == idSito && m.IdTenant == idTenant && m.Attivo, annullamento);
        Aggiungi("MetodiSpedizione", spedizioni > 0, StatoControllo.Bloccante, spedizioni.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Con un canale a consenso OAuth (ADR-0021) non basta l'app: serve anche una casella collegata e con il consenso valido.
        var canaleEmail = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(i => i.IdTenant == idTenant
                && (i.Provider != ProviderEmail.MicrosoftOAuth && i.Provider != ProviderEmail.GoogleOAuth
                    || _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().Any(c => c.IdTenant == idTenant && c.Stato == StatoConnessioneEmail.Attiva)), annullamento);
        Aggiungi("CanaleEmail", canaleEmail, StatoControllo.Attenzione);

        var amministratori = await _contesto.Utenze.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(u => u.IdTenant == idTenant && u.Ruolo == RuoloUtenza.TenantAdmin && u.Stato == StatoUtenza.Attiva, annullamento);
        Aggiungi("AmministratoriAttivi", amministratori > 0, StatoControllo.Attenzione, amministratori.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var slug in new[] { "privacy", "resi" })
        {
            var pubblicata = await _contesto.Pagine.IgnoreQueryFilters().AsNoTracking().AnyAsync(p => p.IdSito == idSito && p.IdTenant == idTenant && p.Slug == slug && p.Stato == StatoPagina.Pubblicata, annullamento);
            Aggiungi(slug == "privacy" ? "PaginaPrivacy" : "PaginaResi", pubblicata, StatoControllo.Attenzione, slug);
        }

        Aggiungi("ManutenzioneAttiva", _manutenzione.Abilitata, StatoControllo.Attenzione);

        var dominio = await _contesto.DominiSito.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.IdSito == idSito && d.IdTenant == idTenant && d.Attivo && !d.SoloSviluppo)
            .OrderBy(d => d.CreatoIlUtc)
            .Select(d => d.Host)
            .FirstOrDefaultAsync(annullamento);
        controlli.Add(dominio is null
            ? new ControlloProntezza("DominioPubblico", StatoControllo.Bloccante)
            : new ControlloProntezza(
                "DominioPubblico",
                StatoControllo.Ok,
                credenzialiNexi is null ? $"https://{dominio}/webhooks/paypal" : $"https://{dominio}/webhooks/paypal, https://{dominio}{ServizioPagamenti.PercorsoNotificaNexi}"));

        return new RapportoProntezza(sito.Nome, ambiente, controlli);
    }
}

using DvCms.Application.Negozio;
using DvCms.Domain.Accessi;
using DvCms.Domain.Cms;
using DvCms.Domain.Email;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Manutenzione;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Pagamenti;

/// <summary>Verifica di prontezza del negozio (vedi <see cref="IServizioProntezzaNegozio"/>).</summary>
public sealed class ServizioProntezzaNegozio : IServizioProntezzaNegozio
{
    private readonly ContestoDatabase _contesto;
    private readonly IArchivioCredenzialiPagamento _credenziali;
    private readonly OpzioniNegozio _opzioni;
    private readonly OpzioniManutenzione _manutenzione;

    public ServizioProntezzaNegozio(ContestoDatabase contesto, IArchivioCredenzialiPagamento credenziali, OpzioniNegozio opzioni, OpzioniManutenzione manutenzione)
    {
        _contesto = contesto;
        _credenziali = credenziali;
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
        var ambiente = impostazioni?.Ambiente ?? AmbientePagamento.Sandbox;
        var credenziali = await _credenziali.OttieniAsync(idTenant, idSito, annullamento);
        Aggiungi("CredenzialiPayPal", credenziali is not null, StatoControllo.Bloccante, ambiente.ToString());

        // L'identificativo del webhook fa parte delle credenziali complete: se si leggono, c'è. Senza, i pagamenti non si concludono da soli.
        Aggiungi("WebhookPayPal", credenziali is not null && !string.IsNullOrEmpty(credenziali.IdWebhook), StatoControllo.Bloccante);

        if (ambiente == AmbientePagamento.Live)
        {
            Aggiungi("InterruttoreProduzione", _opzioni.CheckoutProduzioneAbilitato, StatoControllo.Bloccante);
        }
        else
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
            : new ControlloProntezza("DominioPubblico", StatoControllo.Ok, $"https://{dominio}/webhooks/paypal"));

        return new RapportoProntezza(sito.Nome, ambiente, controlli);
    }
}

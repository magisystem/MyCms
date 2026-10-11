using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Areas.Platform.Pages.Tenant;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DettaglioModel : PaginaPiattaforma
{
    private readonly IServizioPiattaforma _piattaforma;

    public DettaglioModel(IServizioPiattaforma piattaforma, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _piattaforma = piattaforma;
    }

    public DettaglioTenant? Tenant { get; private set; }

    /// <summary>Password temporanea appena generata: mostrata una sola volta, non viene salvata né registrata.</summary>
    public string? PasswordTemporanea { get; private set; }

    public string? EmailNuovoAmministratore { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        Tenant = await _piattaforma.LeggiTenantAsync(IdAttore, id, annullamento);
        return Tenant is null ? NotFound() : Page();
    }

    /// <summary>Indirizzo dell'admin del tenant sul suo dominio (l'accesso è per host: serve accedere da lì).</summary>
    public string IndirizzoAdmin(string host)
    {
        var porta = Request.Host.Port is { } valore and not (80 or 443) ? $":{valore}" : string.Empty;
        return $"{Request.Scheme}://{host}{porta}/admin";
    }

    public Task<IActionResult> OnPostRinominaAsync(Guid id, string? nome, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.RinominaTenantAsync(IdAttore, id, nome, annullamento), Localizzatore.Traduci("Nome aggiornato."));

    public Task<IActionResult> OnPostSospendiAsync(Guid id, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ImpostaTenantSospesoAsync(IdAttore, id, true, annullamento), Localizzatore.Traduci("Tenant sospeso."));

    public Task<IActionResult> OnPostRiattivaAsync(Guid id, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ImpostaTenantSospesoAsync(IdAttore, id, false, annullamento), Localizzatore.Traduci("Tenant riattivato."));

    public Task<IActionResult> OnPostNuovoSitoAsync(Guid id, string? nome, string? fusoOrario, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.CreaSitoAsync(IdAttore, id, nome, fusoOrario, annullamento), Localizzatore.Traduci("Sito creato."));

    public Task<IActionResult> OnPostModificaSitoAsync(
        Guid id,
        Guid idSito,
        string? nome,
        string? fusoOrario,
        bool attivo,
        CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ModificaSitoAsync(IdAttore, id, idSito, nome, fusoOrario, attivo, annullamento), Localizzatore.Traduci("Sito aggiornato."));

    public Task<IActionResult> OnPostCommercioAsync(Guid id, Guid idSito, bool attivo, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ImpostaCommercioAsync(IdAttore, id, idSito, attivo, annullamento), Localizzatore.Traduci("Negozio aggiornato."));

    public Task<IActionResult> OnPostAbilitaLinguaAsync(Guid id, Guid idSito, string? lingua, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.AbilitaLinguaAsync(IdAttore, id, idSito, lingua, annullamento), Localizzatore.Traduci("Lingua abilitata."));

    public Task<IActionResult> OnPostDisabilitaLinguaAsync(Guid id, Guid idSito, string? lingua, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.DisabilitaLinguaAsync(IdAttore, id, idSito, lingua, annullamento), Localizzatore.Traduci("Lingua disabilitata."));

    public Task<IActionResult> OnPostLinguaPredefinitaAsync(Guid id, Guid idSito, string? lingua, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ImpostaLinguaPredefinitaAsync(IdAttore, id, idSito, lingua, annullamento), Localizzatore.Traduci("Lingua predefinita cambiata."));

    public Task<IActionResult> OnPostNuovoDominioAsync(Guid id, Guid idSito, string? host, bool verificato, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.AggiungiDominioAsync(IdAttore, id, idSito, host, verificato, annullamento), Localizzatore.Traduci("Dominio aggiunto."));

    public Task<IActionResult> OnPostVerificaDominioAsync(Guid id, Guid idDominio, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.VerificaDominioAsync(IdAttore, id, idDominio, annullamento), Localizzatore.Traduci("Dominio verificato."));

    public Task<IActionResult> OnPostDominioAttivoAsync(Guid id, Guid idDominio, bool attivo, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ImpostaDominioAttivoAsync(IdAttore, id, idDominio, attivo, annullamento), Localizzatore.Traduci("Dominio aggiornato."));

    public Task<IActionResult> OnPostPromuoviAsync(Guid id, Guid idUtenza, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ElevaATenantAdminAsync(IdAttore, id, idUtenza, annullamento), Localizzatore.Traduci("Account promosso ad amministratore del tenant."));

    public Task<IActionResult> OnPostAccountAttivoAsync(Guid id, Guid idUtenza, bool attiva, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ImpostaUtenzaAttivaAsync(IdAttore, id, idUtenza, attiva, annullamento), Localizzatore.Traduci("Account aggiornato."));

    public Task<IActionResult> OnPostReimpostaDueFattoriAsync(Guid id, Guid idUtenza, CancellationToken annullamento) =>
        EseguiAsync(id, () => _piattaforma.ReimpostaDueFattoriAsync(IdAttore, id, idUtenza, annullamento), Localizzatore.Traduci("Autenticazione a due fattori reimpostata: l'account dovrà configurarla di nuovo."));

    public async Task<IActionResult> OnPostInviaInvitoAsync(Guid id, Guid idUtenza, CancellationToken annullamento) =>
        await EseguiAsync(id, () => _piattaforma.InviaInvitoAsync(IdAttore, id, idUtenza, LinkInvito(), annullamento), Localizzatore.Traduci("Invito inviato."));

    public async Task<IActionResult> OnPostNuovoAmministratoreAsync(Guid id, string? email, string? nomeVisualizzato, bool conInvito, CancellationToken annullamento)
    {
        if (conInvito)
        {
            var invito = await _piattaforma.CreaTenantAdminConInvitoAsync(IdAttore, id, email, nomeVisualizzato, LinkInvito(), annullamento);
            if (invito.Riuscito)
            {
                Messaggio = Localizzatore.Traduci("Amministratore creato: l'invito è stato accodato per l'invio via email.");
            }
            else
            {
                MessaggioErrore = TestoErrore(invito.Codice);
            }

            return RedirectToPage(new { id });
        }

        var esito = await _piattaforma.CreaTenantAdminAsync(IdAttore, id, email, nomeVisualizzato, annullamento);
        if (!esito.Riuscito)
        {
            MessaggioErrore = TestoErrore(esito.Codice);
            return RedirectToPage(new { id });
        }

        // La password si mostra qui, una volta sola, senza reindirizzare (non passa da TempData né dall'URL).
        PasswordTemporanea = esito.PasswordTemporanea;
        EmailNuovoAmministratore = email?.Trim();
        Tenant = await _piattaforma.LeggiTenantAsync(IdAttore, id, annullamento);
        return Tenant is null ? NotFound() : Page();
    }

    /// <summary>Schema e porta della richiesta corrente: il link dell'invito usa l'host del dominio del tenant.</summary>
    private DatiLinkInvito LinkInvito() => new(Request.Scheme, Request.Host.Port);

    private async Task<IActionResult> EseguiAsync(Guid id, Func<Task<EsitoOperazione>> operazione, string messaggioRiuscito)
    {
        var esito = await operazione();
        if (esito.Riuscito)
        {
            Messaggio = messaggioRiuscito;
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage(new { id });
    }
}

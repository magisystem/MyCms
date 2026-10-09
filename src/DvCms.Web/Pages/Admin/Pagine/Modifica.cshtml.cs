using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Cms;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Pagine;

public class ModificaModel : PaginaAdmin
{
    private readonly IServizioPagine _pagine;

    private readonly RegistroModelliPagina _modelli;

    public ModificaModel(IServizioPagine pagine, RegistroModelliPagina modelli, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _pagine = pagine;
        _modelli = modelli;
    }

    /// <summary>Il modello (nella versione della bozza) con cui si compone il modulo.</summary>
    public ModelloPagina? Modello => Pagina is null ? null : _modelli.Trova(Pagina.IdModello, Pagina.VersioneModello);

    public IReadOnlyCollection<string> CampiNonValidi { get; private set; } = [];

    public DettaglioPagina? Pagina { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken annullamento)
    {
        Pagina = await _pagine.LeggiAsync(IdAttore, id, annullamento);
        return Pagina is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(
        Guid id,
        string? titolo,
        string? slug,
        string? contenuto,
        string? nota,
        bool paginaIniziale,
        string? tokenConcorrenza,
        string? titoloSeo,
        string? descrizioneSeo,
        bool nonIndicizzare,
        CancellationToken annullamento)
    {
        var datiModello = Request.Form
            .Where(campo => campo.Key.StartsWith("m_", StringComparison.Ordinal))
            .ToDictionary(campo => campo.Key[2..], campo => campo.Value.ToString(), StringComparer.Ordinal);
        var esito = await _pagine.SalvaAsync(
            IdAttore,
            id,
            new DatiModificaPagina(slug, titolo, contenuto, nota, paginaIniziale, tokenConcorrenza, datiModello, titoloSeo, descrizioneSeo, nonIndicizzare),
            annullamento);
        if (esito.Codice == CodiceEsito.DatiNonValidi && esito.CampiNonValidi is { Count: > 0 } campi)
        {
            // Si ripropone il modulo con ciò che l'utente ha scritto e i campi da correggere.
            Pagina = await _pagine.LeggiAsync(IdAttore, id, annullamento);
            if (Pagina is null)
            {
                return NotFound();
            }

            Pagina = Pagina with
            {
                Titolo = titolo ?? string.Empty,
                Contenuto = contenuto ?? string.Empty,
                DatiModello = datiModello,
                TitoloSeo = titoloSeo,
                DescrizioneSeo = descrizioneSeo,
                NonIndicizzare = nonIndicizzare,
            };
            CampiNonValidi = campi;
            Errore = TestoErrore(esito.Codice);
            return Page();
        }

        return Concludi(id, esito, Localizzatore.Traduci("Bozza salvata."));
    }

    public async Task<IActionResult> OnPostEliminaReindirizzamentoAsync(Guid id, Guid idReindirizzamento, CancellationToken annullamento)
    {
        var esito = await _pagine.EliminaReindirizzamentoAsync(IdAttore, id, idReindirizzamento, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Reindirizzamento eliminato."));
    }

    public async Task<IActionResult> OnPostAggiornaModelloAsync(Guid id, string? tokenConcorrenza, CancellationToken annullamento)
    {
        var esito = await _pagine.AggiornaModelloAsync(IdAttore, id, tokenConcorrenza, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Bozza portata all'ultima versione del modello."));
    }

    public async Task<IActionResult> OnPostPubblicaAsync(Guid id, string? tokenConcorrenza, CancellationToken annullamento)
    {
        var esito = await _pagine.PubblicaAsync(IdAttore, id, tokenConcorrenza, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Pagina pubblicata."));
    }

    public async Task<IActionResult> OnPostPubblicaRevisioneAsync(Guid id, Guid idRevisione, string? tokenConcorrenza, CancellationToken annullamento)
    {
        var esito = await _pagine.PubblicaRevisioneAsync(IdAttore, id, idRevisione, tokenConcorrenza, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Revisione pubblicata."));
    }

    public async Task<IActionResult> OnPostRipristinaAsync(Guid id, Guid idRevisione, string? tokenConcorrenza, CancellationToken annullamento)
    {
        var esito = await _pagine.RipristinaComeBozzaAsync(IdAttore, id, idRevisione, tokenConcorrenza, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Revisione ripristinata come nuova bozza."));
    }

    public async Task<IActionResult> OnPostRitiraAsync(Guid id, string? tokenConcorrenza, CancellationToken annullamento)
    {
        var esito = await _pagine.RitiraAsync(IdAttore, id, tokenConcorrenza, annullamento);
        return Concludi(id, esito, Localizzatore.Traduci("Pagina ritirata dal pubblico."));
    }

    private RedirectToPageResult Concludi(Guid id, DvCms.Application.Piattaforma.EsitoOperazione esito, string messaggioRiuscito)
    {
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

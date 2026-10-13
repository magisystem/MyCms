using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Piattaforma;
using DvCms.Core.Localizzazione;
using DvCms.DTO.Piattaforma;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Areas.Platform.Pages.Tenant;

public class NuovoModel : PaginaPiattaforma
{
    private readonly IServizioPiattaforma _piattaforma;

    public NuovoModel(IServizioPiattaforma piattaforma, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _piattaforma = piattaforma;
    }

    [BindProperty]
    public string? Nome { get; set; }

    [BindProperty]
    public string? NomeSito { get; set; }

    [BindProperty]
    public string? FusoOrario { get; set; } = "Europe/Rome";

    [BindProperty]
    public string? Host { get; set; }

    [BindProperty]
    public bool DominioVerificato { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken annullamento)
    {
        var esito = await _piattaforma.CreaTenantAsync(
            IdAttore,
            new DatiNuovoTenant(Nome, NomeSito, FusoOrario, Host, DominioVerificato),
            annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Tenant creato.");
            return RedirectToPage("Dettaglio", new { id = esito.Id });
        }

        Errore = TestoErrore(esito.Codice);
        return Page();
    }
}

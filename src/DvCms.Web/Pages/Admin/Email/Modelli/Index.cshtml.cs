using DvCms.Contracts.Email;
using DvCms.Contracts.Localizzazione;
using DvCms.DTO.Email;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Email.Modelli;

/// <summary>Elenco dei modelli email del tenant (ADR-0025). Solo il <c>TenantAdmin</c> (politica della cartella e riverifica nel servizio).</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PaginaAdmin
{
    private readonly IServizioModelliEmail _modelli;

    public IndexModel(IServizioModelliEmail modelli, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _modelli = modelli;
    }

    public IReadOnlyList<VoceModelloEmail> Voci { get; private set; } = [];

    /// <summary>Le lingue in cui il tenant può scrivere i modelli.</summary>
    public IReadOnlyList<string> Lingue { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        try
        {
            Voci = await _modelli.ElencaAsync(IdAttore, annullamento);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var prima = Voci.Count > 0 ? await _modelli.LeggiAsync(IdAttore, Voci[0].Definizione.Codice, "it", annullamento) : null;
        Lingue = prima?.LingueDisponibili ?? ["it"];
        return Page();
    }
}

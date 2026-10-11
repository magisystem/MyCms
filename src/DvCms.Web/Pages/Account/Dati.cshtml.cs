using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Web.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DvCms.Web.Pages.Account;

/// <summary>
/// I miei dati personali (A7, ADR-0020): copia dei dati in formato JSON e cancellazione dell'account, con riconferma della password.
/// Gli ordini restano conservati per gli obblighi fiscali; quelli non pagati si annullano.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DatiModel : PageModel
{
    private readonly IServizioDatiPersonali _dati;

    public DatiModel(IServizioDatiPersonali dati) => _dati = dati;

    [BindProperty]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [BindProperty]
    public bool Confermo { get; set; }

    public string? Errore { get; private set; }

    /// <summary>Vero dopo la cancellazione: la pagina mostra l'esito e la sessione è chiusa.</summary>
    public bool Eliminato { get; private set; }

    private Guid IdAccount => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetEsportaAsync(CancellationToken annullamento)
    {
        if (await _dati.EsportaMieiAsync(IdAccount, annullamento) is not { } esportazione)
        {
            return NotFound();
        }

        return File(Encoding.UTF8.GetBytes(esportazione.Json), "application/json; charset=utf-8", esportazione.NomeFile);
    }

    public async Task<IActionResult> OnPostEliminaAsync(CancellationToken annullamento)
    {
        if (!Confermo)
        {
            Errore = "confermo";
            return Page();
        }

        var esito = await _dati.AnonimizzaMioAccountAsync(IdAccount, Password, annullamento);
        switch (esito.Codice)
        {
            case CodiceEsito.Riuscito:
                await HttpContext.SignOutAsync(Schemi.Cliente);
                Eliminato = true;
                return Page();
            case CodiceEsito.DatiNonValidi:
                Errore = "password";
                return Page();
            default:
                // Account non eliminabile da qui (ad esempio un amministratore che acquista con la sessione cliente).
                Errore = "nonconsentito";
                return Page();
        }
    }
}

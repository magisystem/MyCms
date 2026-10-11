using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Spedizioni;

public sealed class ModuloMetodoSpedizione
{
    public Guid? Id { get; set; }

    public TipoMetodoSpedizione Tipo { get; set; } = TipoMetodoSpedizione.Zona;

    public AreaSpedizione? Area { get; set; } = AreaSpedizione.Italia;

    public string? Nome { get; set; }

    public string? Tariffa { get; set; }

    public bool Attivo { get; set; } = true;
}

/// <summary>
/// Metodi di spedizione del sito (F-002, ADR-0020): tariffa fissa IVA inclusa per area (Italia, resto dell'UE) e ritiro in sede.
/// 404 se il sito non ha il negozio attivo. Il server rivalida tutto: il modulo non decide mai tipo e tariffa del ritiro.
/// </summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioSpedizioni _spedizioni;
    private readonly IServizioCatalogo _catalogo;

    public IndexModel(IServizioSpedizioni spedizioni, IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _spedizioni = spedizioni;
        _catalogo = catalogo;
    }

    public IReadOnlyList<MetodoSpedizioneVista> Metodi { get; private set; } = [];

    /// <summary>Modulo rimostrato dopo un errore (nuovo metodo se <c>Id</c> è nullo).</summary>
    public ModuloMetodoSpedizione? ModuloErrato { get; private set; }

    public IReadOnlyList<string> CampiNonValidi { get; private set; } = [];

    public CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("it-IT");

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Metodi = await _spedizioni.ElencaAsync(IdAttore, annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(ModuloMetodoSpedizione modulo, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        ArgumentNullException.ThrowIfNull(modulo);
        if (modulo.Tipo == TipoMetodoSpedizione.Ritiro)
        {
            // Il modulo di creazione invia sempre anche l'area: per il ritiro non ha senso e la si scarta.
            modulo.Area = null;
        }

        var tariffaLetta = TentaLeggereTariffa(modulo);
        if (tariffaLetta is null)
        {
            return await RimostraAsync(modulo, ["TariffaLorda"], CodiceEsito.DatiNonValidi, annullamento);
        }

        var esito = await _spedizioni.SalvaAsync(IdAttore, new DatiMetodoSpedizione(modulo.Id, modulo.Tipo, modulo.Area, modulo.Nome, tariffaLetta.Value, modulo.Attivo), annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Metodo di spedizione salvato.");
            return RedirectToPage();
        }

        return await RimostraAsync(modulo, esito.CampiNonValidi ?? [], esito.Codice, annullamento);
    }

    public async Task<IActionResult> OnPostEliminaAsync(Guid id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        var esito = await _spedizioni.EliminaAsync(IdAttore, id, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Metodo di spedizione eliminato.");
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage();
    }

    public string NomeCampo(string campo) => campo switch
    {
        "Nome" => Localizzatore.Traduci("Nome"),
        "Area" => Localizzatore.Traduci("Area"),
        "TariffaLorda" => Localizzatore.Traduci("Tariffa (EUR, IVA inclusa)"),
        "Tipo" => Localizzatore.Traduci("Tipo"),
        _ => campo,
    };

    public string NomeArea(AreaSpedizione? area) => area switch
    {
        AreaSpedizione.Italia => Localizzatore.Traduci("Italia"),
        AreaSpedizione.UnioneEuropea => Localizzatore.Traduci("Resto dell'Unione europea"),
        _ => string.Empty,
    };

    public bool Errato(Guid? id) => ModuloErrato is not null && ModuloErrato.Id == id;

    /// <summary>La tariffa del ritiro non si digita: vale sempre zero. Vuoto equivale a zero (spedizione gratuita).</summary>
    private static decimal? TentaLeggereTariffa(ModuloMetodoSpedizione modulo)
    {
        if (modulo.Tipo == TipoMetodoSpedizione.Ritiro || string.IsNullOrWhiteSpace(modulo.Tariffa))
        {
            return 0m;
        }

        var testo = modulo.Tariffa.Trim().Replace(',', '.');
        return decimal.TryParse(testo, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valore) ? valore : null;
    }

    private async Task<IActionResult> RimostraAsync(ModuloMetodoSpedizione modulo, IReadOnlyList<string> campi, CodiceEsito codice, CancellationToken annullamento)
    {
        Metodi = await _spedizioni.ElencaAsync(IdAttore, annullamento);
        ModuloErrato = modulo;
        CampiNonValidi = campi;
        Errore = codice == CodiceEsito.NonConsentito
            ? Localizzatore.Traduci("Non è possibile aggiungere altri metodi di spedizione.")
            : TestoErrore(codice);
        return Page();
    }
}

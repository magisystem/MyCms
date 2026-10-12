using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Categorie;

public sealed class ModuloCategoria
{
    public Guid? Id { get; set; }

    public string? Posizione { get; set; }

    /// <summary>Nome per lingua (codice → nome).</summary>
    public Dictionary<string, string> Nomi { get; set; } = [];
}

/// <summary>
/// Categorie (collezioni) del negozio: un elenco piatto con nome per lingua e posizione. 404 se il sito non ha il negozio attivo. Il
/// server rivalida tutto; eliminare una categoria toglie solo l'appartenenza dei prodotti.
/// </summary>
public class IndexModel : PaginaAdmin
{
    private readonly IServizioCategorie _categorie;
    private readonly IServizioCatalogo _catalogo;

    public IndexModel(IServizioCategorie categorie, IServizioCatalogo catalogo, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _categorie = categorie;
        _catalogo = catalogo;
    }

    public IReadOnlyList<CategoriaVista> Categorie { get; private set; } = [];

    public IReadOnlyList<LinguaCatalogo> Lingue { get; private set; } = [];

    /// <summary>Modulo rimostrato dopo un errore (nuova categoria se <c>Id</c> è nullo).</summary>
    public ModuloCategoria? ModuloErrato { get; private set; }

    public IReadOnlyList<string> CampiNonValidi { get; private set; } = [];

    public int Massimo => Categoria.NumeroMassimo;

    public async Task<IActionResult> OnGetAsync(CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        await CaricaAsync(annullamento);
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(ModuloCategoria modulo, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        ArgumentNullException.ThrowIfNull(modulo);
        if (!int.TryParse(string.IsNullOrWhiteSpace(modulo.Posizione) ? "0" : modulo.Posizione.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var posizione))
        {
            return await RimostraAsync(modulo, ["Posizione"], CodiceEsito.DatiNonValidi, annullamento);
        }

        var nomi = modulo.Nomi.Select(n => new NomeCategoriaDati(n.Key, n.Value ?? string.Empty)).ToList();
        var esito = await _categorie.SalvaAsync(IdAttore, new DatiCategoria(modulo.Id, posizione, nomi), annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Categoria salvata.");
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

        var esito = await _categorie.EliminaAsync(IdAttore, id, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Categoria eliminata: i prodotti restano.");
        }
        else
        {
            MessaggioErrore = TestoErrore(esito.Codice);
        }

        return RedirectToPage();
    }

    public string NomeCampo(string campo) => campo switch
    {
        "Nome" => Localizzatore.Traduci("Nome (obbligatorio nella lingua predefinita)"),
        "Nomi" => Localizzatore.Traduci("Nomi"),
        "Posizione" => Localizzatore.Traduci("Posizione"),
        _ => campo,
    };

    public bool Errato(Guid? id) => ModuloErrato is not null && ModuloErrato.Id == id;

    private async Task CaricaAsync(CancellationToken annullamento)
    {
        Lingue = await _catalogo.LingueAsync(IdAttore, annullamento);
        Categorie = await _categorie.ElencaAsync(IdAttore, annullamento);
    }

    private async Task<IActionResult> RimostraAsync(ModuloCategoria modulo, IReadOnlyList<string> campi, CodiceEsito codice, CancellationToken annullamento)
    {
        await CaricaAsync(annullamento);
        ModuloErrato = modulo;
        CampiNonValidi = campi;
        Errore = codice == CodiceEsito.NonConsentito
            ? Localizzatore.Traduci("Non è possibile aggiungere altre categorie.")
            : TestoErrore(codice);
        return Page();
    }
}

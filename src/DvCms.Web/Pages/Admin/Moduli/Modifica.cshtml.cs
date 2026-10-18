using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Moduli;
using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Moduli;
using DvCms.Web.AntiAbuso;
using DvCms.Web.Moduli;
using DvCms.Web.Pubblico;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Moduli;

/// <summary>
/// Creazione e modifica di un modulo di contatto (ADR-0027). Aggiungere, togliere e spostare i campi ricarica la pagina senza salvare:
/// lo stato sta nella form. Il servizio rivalida tutto; l'anteprima mostra il modulo come lo vedrà il visitatore, con il pulsante spento.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ModificaModel : PaginaAdmin
{
    private readonly IServizioModuli _moduli;
    private readonly IVerificaAntiAbuso _antiAbuso;

    public ModificaModel(IServizioModuli moduli, IVerificaAntiAbuso antiAbuso, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _moduli = moduli;
        _antiAbuso = antiAbuso;
    }

    [BindProperty]
    public InputModulo Input { get; set; } = new();

    /// <summary>Identificativo del modulo in modifica; nullo per un modulo nuovo.</summary>
    public Guid? IdModulo { get; private set; }

    public IReadOnlyList<LinguaModulo> Lingue { get; private set; } = [];

    public string LinguaPredefinita => Lingue.First(l => l.Predefinita).Codice;

    public IReadOnlyList<string> ErroriModulo { get; private set; } = [];

    public ModelloFormModulo? Anteprima { get; private set; }

    public int RichiesteTotali { get; private set; }

    public bool ProtezioneAttiva => _antiAbuso.Attiva;

    public static IReadOnlyList<TipoCampoModulo> Tipi { get; } = Enum.GetValues<TipoCampoModulo>();

    public string NomeTipo(TipoCampoModulo tipo) => tipo switch
    {
        TipoCampoModulo.TestoBreve => Localizzatore.Traduci("Testo breve"),
        TipoCampoModulo.TestoLungo => Localizzatore.Traduci("Testo lungo"),
        TipoCampoModulo.Email => Localizzatore.Traduci("Email"),
        TipoCampoModulo.Telefono => Localizzatore.Traduci("Telefono"),
        TipoCampoModulo.Numero => Localizzatore.Traduci("Numero"),
        TipoCampoModulo.Data => Localizzatore.Traduci("Data"),
        TipoCampoModulo.SceltaSingola => Localizzatore.Traduci("Scelta singola"),
        TipoCampoModulo.SceltaMultipla => Localizzatore.Traduci("Scelta multipla"),
        _ => Localizzatore.Traduci("Consenso"),
    };

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken annullamento)
    {
        if (!await CaricaLingueAsync(annullamento))
        {
            return Forbid();
        }

        if (id is null)
        {
            Input = InputModulo.Nuovo(LinguaPredefinita);
            Input.AllineaLingue(Lingue);
            return Page();
        }

        var vista = await _moduli.LeggiAsync(IdAttore, id.Value, annullamento);
        if (vista is null)
        {
            return NotFound();
        }

        IdModulo = id;
        RichiesteTotali = vista.RichiesteTotali;
        Input = InputModulo.Da(vista, Lingue);
        return Page();
    }

    public async Task<IActionResult> OnPostSalvaAsync(Guid? id, CancellationToken annullamento)
    {
        if (!await PreparaAsync(id, annullamento))
        {
            return Forbid();
        }

        var (dati, errori) = Input.Converti(Lingue);
        if (dati is null)
        {
            ErroriModulo = errori;
            return Page();
        }

        var esito = id is null
            ? await _moduli.CreaAsync(IdAttore, dati, annullamento)
            : await _moduli.AggiornaAsync(IdAttore, id.Value, dati, Input.TokenConcorrenza, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Modulo salvato.");
            return Redirect("/admin/moduli/modifica/" + esito.Id);
        }

        if (esito.Codice == CodiceEsito.NonTrovato)
        {
            return NotFound();
        }

        ErroriModulo = esito.Errori.Count > 0 ? esito.Errori : [TestoErrore(esito.Codice)];
        return Page();
    }

    public async Task<IActionResult> OnPostAnteprimaAsync(Guid? id, CancellationToken annullamento)
    {
        if (!await PreparaAsync(id, annullamento))
        {
            return Forbid();
        }

        var (dati, errori) = Input.Converti(Lingue);
        if (dati is null)
        {
            ErroriModulo = errori;
            return Page();
        }

        Anteprima = new ModelloFormModulo(null, dati.Definizione, LinguaPredefinita, LinguaPredefinita, Anteprima: true, Azione: null, ConProvaAntiAbuso: false);
        return Page();
    }

    public async Task<IActionResult> OnPostAggiungiCampoAsync(Guid? id, string? nuovoTipo, CancellationToken annullamento)
    {
        if (!await PreparaAsync(id, annullamento))
        {
            return Forbid();
        }

        if (Input.Campi.Count >= DefinizioneModulo.NumeroMassimoCampi)
        {
            ErroriModulo = [$"Un modulo può avere al massimo {DefinizioneModulo.NumeroMassimoCampi} campi."];
            return Page();
        }

        var tipo = Enum.TryParse<TipoCampoModulo>(nuovoTipo, ignoreCase: false, out var letto) && Enum.IsDefined(letto) ? letto : TipoCampoModulo.TestoBreve;
        var campo = CampoModulo.Nuovo(Input.ProssimoIdCampo(), tipo, LinguaPredefinita);
        Input.Campi.Add(new InputCampo
        {
            Id = campo.Id,
            Tipo = tipo.ToString(),
            Obbligatorio = campo.Obbligatorio,
            LunghezzaMassima = campo.LunghezzaMassima.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        Input.AllineaLingue(Lingue);
        return Page();
    }

    public async Task<IActionResult> OnPostRimuoviCampoAsync(Guid? id, int indice, CancellationToken annullamento)
    {
        if (!await PreparaAsync(id, annullamento))
        {
            return Forbid();
        }

        if (indice >= 0 && indice < Input.Campi.Count)
        {
            Input.Campi.RemoveAt(indice);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSpostaCampoAsync(Guid? id, int indice, int verso, CancellationToken annullamento)
    {
        if (!await PreparaAsync(id, annullamento))
        {
            return Forbid();
        }

        var destinazione = indice + (verso < 0 ? -1 : 1);
        if (indice >= 0 && indice < Input.Campi.Count && destinazione >= 0 && destinazione < Input.Campi.Count)
        {
            (Input.Campi[indice], Input.Campi[destinazione]) = (Input.Campi[destinazione], Input.Campi[indice]);
        }

        return Page();
    }

    /// <summary>Carica le lingue e riallinea l'input ricevuto; falso se l'attore non può gestire i moduli. Le operazioni sui campi non salvano nulla.</summary>
    private async Task<bool> PreparaAsync(Guid? id, CancellationToken annullamento)
    {
        if (!await CaricaLingueAsync(annullamento))
        {
            return false;
        }

        IdModulo = id;
        ModelState.Clear();
        Input ??= InputModulo.Nuovo(LinguaPredefinita);
        Input.AllineaLingue(Lingue);
        return true;
    }

    private async Task<bool> CaricaLingueAsync(CancellationToken annullamento)
    {
        try
        {
            Lingue = await _moduli.LingueAsync(IdAttore, annullamento);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

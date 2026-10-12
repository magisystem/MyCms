using System.Globalization;
using DvCms.Application.Localizzazione;
using DvCms.Application.Media;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using Microsoft.AspNetCore.Mvc;

namespace DvCms.Web.Pages.Admin.Negozio.Prodotti;

public sealed class ModuloTraduzione
{
    public string Codice { get; set; } = string.Empty;

    public string? Nome { get; set; }

    public string? Descrizione { get; set; }
}

public sealed class ModuloVariante
{
    public Guid? Id { get; set; }

    public string? Sku { get; set; }

    public string? Prezzo { get; set; }

    public bool Attiva { get; set; } = true;

    public Dictionary<string, string> Etichette { get; set; } = [];

    public bool EVuota => string.IsNullOrWhiteSpace(Sku) && string.IsNullOrWhiteSpace(Prezzo) && Etichette.Values.All(string.IsNullOrWhiteSpace);
}

public sealed class ModuloProdotto
{
    public string? Token { get; set; }

    public StatoProdotto Stato { get; set; } = StatoProdotto.Bozza;

    public string Aliquota { get; set; } = "22";

    public List<ModuloTraduzione> Traduzioni { get; set; } = [];

    public List<ModuloVariante> Varianti { get; set; } = [];

    /// <summary>Posizione scelta per ogni immagine della libreria (identificativo → numero); vuoto = non usata.</summary>
    public Dictionary<Guid, string> Immagini { get; set; } = [];

    /// <summary>Categorie a cui appartiene il prodotto.</summary>
    public List<Guid> Categorie { get; set; } = [];
}

/// <summary>
/// Creazione e modifica di un prodotto (F-001, F-006): stato, aliquota IVA, nome e descrizione per lingua, varianti con codice, prezzo
/// IVA inclusa ed etichette. Il server rivalida tutto e non si fida di nulla che arrivi dal modulo (prezzi compresi).
/// </summary>
public class ModificaModel : PaginaAdmin
{
    private const int RigheVuoteAggiuntive = 3;

    private readonly IServizioCatalogo _catalogo;
    private readonly IServizioMedia _media;
    private readonly IServizioCategorie _categorie;

    public ModificaModel(IServizioCatalogo catalogo, IServizioMedia media, IServizioCategorie categorie, ILocalizzatore localizzatore)
        : base(localizzatore)
    {
        _catalogo = catalogo;
        _media = media;
        _categorie = categorie;
    }

    /// <summary>Le categorie del sito fra cui scegliere.</summary>
    public IReadOnlyList<CategoriaVista> CategorieDisponibili { get; private set; } = [];

    /// <summary>Nome di una categoria nella lingua predefinita del sito (o nella prima disponibile).</summary>
    public string NomeCategoria(CategoriaVista categoria) =>
        (categoria.Nomi.FirstOrDefault(n => Lingue.Any(l => l.Predefinita && l.Codice == n.CodiceLingua)) ?? (categoria.Nomi.Count > 0 ? categoria.Nomi[0] : null))?.Nome ?? "—";

    /// <summary>Le immagini pubbliche della libreria fra cui scegliere.</summary>
    public IReadOnlyList<RiepilogoMedia> ImmaginiDisponibili { get; private set; } = [];

    [BindProperty]
    public ModuloProdotto Modulo { get; set; } = new();

    public IReadOnlyList<LinguaCatalogo> Lingue { get; private set; } = [];

    public bool Nuovo { get; private set; }

    public IReadOnlyList<string> CampiNonValidi { get; private set; } = [];

    public IReadOnlyList<decimal> AliquoteAmmesse => AliquoteIva.Ammesse;

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Lingue = await _catalogo.LingueAsync(IdAttore, annullamento);
        if (string.Equals(id, "nuovo", StringComparison.Ordinal))
        {
            Nuovo = true;
            Modulo = new ModuloProdotto { Aliquota = Formatta(AliquoteIva.Predefinita) };
        }
        else
        {
            if (!Guid.TryParse(id, out var idProdotto) || await _catalogo.LeggiAsync(IdAttore, idProdotto, annullamento) is not { } dettaglio)
            {
                return NotFound();
            }

            Modulo = Da(dettaglio);
        }

        await CaricaImmaginiAsync(annullamento);
        Completa();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken annullamento)
    {
        if (!await _catalogo.CommercioAttivoAsync(annullamento))
        {
            return NotFound();
        }

        Nuovo = string.Equals(id, "nuovo", StringComparison.Ordinal);
        Guid? idProdotto = null;
        if (!Nuovo)
        {
            if (!Guid.TryParse(id, out var letto))
            {
                return NotFound();
            }

            idProdotto = letto;
        }

        Lingue = await _catalogo.LingueAsync(IdAttore, annullamento);
        var campiPrezzo = new List<string>();
        var dati = Costruisci(idProdotto, campiPrezzo);
        if (dati is null)
        {
            CampiNonValidi = campiPrezzo;
            Errore = TestoErrore(CodiceEsito.DatiNonValidi);
            await CaricaImmaginiAsync(annullamento);
            Completa();
            return Page();
        }

        var esito = await _catalogo.SalvaAsync(IdAttore, dati, annullamento);
        if (esito.Riuscito)
        {
            Messaggio = Localizzatore.Traduci("Prodotto salvato.");
            return Redirect($"/admin/negozio/prodotti/{esito.Id}");
        }

        CampiNonValidi = esito.CampiNonValidi ?? [];
        Errore = esito.Codice switch
        {
            CodiceEsito.GiaPresente => Localizzatore.Traduci("Un codice articolo è già usato da un'altra variante del sito."),
            CodiceEsito.NonTrovato => Localizzatore.Traduci("Prodotto non trovato."),
            _ => TestoErrore(esito.Codice),
        };
        await CaricaImmaginiAsync(annullamento);
        Completa();
        return Page();
    }

    public string NomeCampo(string campo) => campo switch
    {
        "AliquotaIva" => Localizzatore.Traduci("Aliquota IVA"),
        "Prezzo" => Localizzatore.Traduci("Prezzo"),
        "Valuta" => Localizzatore.Traduci("Valuta"),
        "Sku" => Localizzatore.Traduci("Codice articolo"),
        "Varianti" => Localizzatore.Traduci("Varianti"),
        "Nome" => Localizzatore.Traduci("Nome (obbligatorio nella lingua predefinita)"),
        "Descrizione" => Localizzatore.Traduci("Descrizione"),
        "Traduzioni" => Localizzatore.Traduci("Traduzioni"),
        "Stato" => Localizzatore.Traduci("Stato"),
        "Categorie" => Localizzatore.Traduci("Categorie"),
        "Immagini" => Localizzatore.Traduci("Immagini (al più 8, senza ripetizioni, pubbliche)"),
        "Etichetta" => Localizzatore.Traduci("Etichetta della variante (obbligatoria nella lingua predefinita se ci sono più varianti)"),
        _ => campo,
    };

    public string Formatta(decimal valore) => valore.ToString("0.##", CultureInfo.InvariantCulture);

    private static ModuloProdotto Da(DettaglioProdotto dettaglio) => new()
    {
        Token = dettaglio.TokenConcorrenza,
        Stato = dettaglio.Stato,
        Aliquota = dettaglio.AliquotaIva.ToString("0.##", CultureInfo.InvariantCulture),
        Categorie = [.. dettaglio.Categorie],
        Immagini = dettaglio.Immagini.Select((id, indice) => (id, indice)).ToDictionary(x => x.id, x => (x.indice + 1).ToString(CultureInfo.InvariantCulture)),
        Traduzioni = [.. dettaglio.Traduzioni.Select(t => new ModuloTraduzione { Codice = t.CodiceLingua, Nome = t.Nome, Descrizione = t.Descrizione })],
        Varianti =
        [
            .. dettaglio.Varianti.Select(v => new ModuloVariante
            {
                Id = v.Id,
                Sku = v.Sku,
                Prezzo = v.PrezzoLordo.ToString("0.00", CultureInfo.InvariantCulture),
                Attiva = v.Attiva,
                Etichette = new Dictionary<string, string>(v.Etichette),
            }),
        ],
    };

    /// <summary>Un riga per lingua e qualche riga vuota per le varianti nuove (il modulo non usa JavaScript).</summary>
    private async Task CaricaImmaginiAsync(CancellationToken annullamento)
    {
        CategorieDisponibili = await _categorie.ElencaAsync(IdAttore, annullamento);
        ImmaginiDisponibili = [.. (await _media.ElencaAsync(IdAttore, annullamento))
            .Where(r => r.TipoContenuto.StartsWith("image/", StringComparison.Ordinal) && r.Visibilita == Domain.Media.VisibilitaMedia.Pubblica)];
    }

    private void Completa()
    {
        foreach (var lingua in Lingue.Where(l => Modulo.Traduzioni.All(t => t.Codice != l.Codice)))
        {
            Modulo.Traduzioni.Add(new ModuloTraduzione { Codice = lingua.Codice });
        }

        Modulo.Traduzioni = [.. Lingue.Select(l => Modulo.Traduzioni.First(t => t.Codice == l.Codice))];
        Modulo.Varianti = [.. Modulo.Varianti.Where(v => !v.EVuota || v.Id is not null)];
        for (var i = 0; i < RigheVuoteAggiuntive && Modulo.Varianti.Count < Prodotto.NumeroMassimoVarianti; i++)
        {
            Modulo.Varianti.Add(new ModuloVariante());
        }

        foreach (var variante in Modulo.Varianti)
        {
            foreach (var lingua in Lingue)
            {
                variante.Etichette.TryAdd(lingua.Codice, string.Empty);
            }
        }
    }

    /// <summary>Trasforma il modulo nei dati del servizio; nullo se un prezzo non è un numero (l'errore dice quale campo).</summary>
    private DatiProdotto? Costruisci(Guid? idProdotto, List<string> campiNonValidi)
    {
        if (!decimal.TryParse(Modulo.Aliquota?.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var aliquota))
        {
            campiNonValidi.Add("AliquotaIva");
        }

        var varianti = new List<VarianteDati>();
        foreach (var variante in Modulo.Varianti.Where(v => !v.EVuota))
        {
            if (!decimal.TryParse(variante.Prezzo?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var prezzo))
            {
                prezzo = -1m;
                if (!campiNonValidi.Contains("Prezzo"))
                {
                    campiNonValidi.Add("Prezzo");
                }
            }

            varianti.Add(new VarianteDati(variante.Id, variante.Sku, prezzo, null, variante.Attiva, variante.Etichette));
        }

        // Immagini: ogni riga con un numero è scelta, in ordine di numero (a parità, l'ordine della libreria); la prima è la principale.
        var scelte = new List<(int Posizione, Guid Id)>();
        foreach (var (idRisorsa, testo) in Modulo.Immagini.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
        {
            if (!int.TryParse(testo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var posizione) || posizione is < 1 or > 99)
            {
                campiNonValidi.Add("Immagini");
                break;
            }

            scelte.Add((posizione, idRisorsa));
        }

        if (campiNonValidi.Count > 0)
        {
            return null;
        }

        return new DatiProdotto(
            idProdotto,
            Modulo.Token,
            Modulo.Stato,
            aliquota,
            [.. Modulo.Traduzioni.Where(t => !string.IsNullOrWhiteSpace(t.Nome) || !string.IsNullOrWhiteSpace(t.Descrizione)).Select(t => new TraduzioneProdottoDati(t.Codice, t.Nome ?? string.Empty, t.Descrizione))],
            varianti,
            [.. scelte.OrderBy(x => x.Posizione).ThenBy(x => x.Id).Select(x => x.Id)],
            [.. Modulo.Categorie.Distinct()]);
    }
}

using System.Globalization;
using DvCms.DTO.Moduli;
using DvCms.Models.Moduli;

namespace DvCms.Web.Moduli;

/// <summary>Un campo così come arriva dalla form di modifica (tutto testo: la conversione verifica e il servizio rivalida).</summary>
public sealed class InputCampo
{
    public string? Id { get; set; }

    public string? Tipo { get; set; }

    public bool Obbligatorio { get; set; }

    public string? LunghezzaMassima { get; set; }

    /// <summary>Testi per lingua (codice → testo).</summary>
    public Dictionary<string, string> Etichette { get; set; } = [];

    public Dictionary<string, string> Aiuti { get; set; } = [];

    /// <summary>Opzioni per lingua: un'opzione per riga, nello stesso ordine in ogni lingua.</summary>
    public Dictionary<string, string> Opzioni { get; set; } = [];
}

/// <summary>Il modulo così come arriva dalla form di modifica.</summary>
public sealed class InputModulo
{
    public string? Nome { get; set; }

    public string? Stato { get; set; }

    public string? TokenConcorrenza { get; set; }

    public Dictionary<string, string> Pulsante { get; set; } = [];

    public Dictionary<string, string> Conferma { get; set; } = [];

    public string? PercorsoDopoInvio { get; set; }

    public string? UrlInformativa { get; set; }

    public List<InputCampo> Campi { get; set; } = [];

    /// <summary>Indirizzi separati da virgola, punto e virgola o a capo.</summary>
    public string? Destinatari { get; set; }

    public bool TuttiGliAmministratori { get; set; }

    public bool RispostaAutomatica { get; set; }

    public string? GiorniConservazione { get; set; }

    public static InputModulo Nuovo(string linguaPredefinita) =>
        new()
        {
            Stato = nameof(StatoModulo.Bozza),
            TuttiGliAmministratori = true,
            GiorniConservazione = Modulo.GiorniConservazionePredefiniti.ToString(CultureInfo.InvariantCulture),
            Pulsante = new Dictionary<string, string> { [linguaPredefinita] = string.Empty },
            Conferma = new Dictionary<string, string> { [linguaPredefinita] = string.Empty },
        };

    public static InputModulo Da(VistaModulo vista, IReadOnlyList<LinguaModulo> lingue) =>
        new()
        {
            Nome = vista.Nome,
            Stato = vista.Stato.ToString(),
            TokenConcorrenza = vista.TokenConcorrenza,
            Pulsante = Testi(vista.Definizione.Pulsante, lingue),
            Conferma = Testi(vista.Definizione.Conferma, lingue),
            PercorsoDopoInvio = vista.Definizione.PercorsoDopoInvio,
            UrlInformativa = vista.Definizione.UrlInformativa,
            Campi = [.. vista.Definizione.Campi.Select(campo => new InputCampo
            {
                Id = campo.Id,
                Tipo = campo.Tipo.ToString(),
                Obbligatorio = campo.Obbligatorio,
                LunghezzaMassima = campo.LunghezzaMassima.ToString(CultureInfo.InvariantCulture),
                Etichette = Testi(campo.Etichette, lingue),
                Aiuti = Testi(campo.Aiuti, lingue),
                Opzioni = lingue.ToDictionary(
                    l => l.Codice,
                    l => string.Join('\n', campo.Opzioni.Select(o => o.GetValueOrDefault(l.Codice) ?? string.Empty)),
                    StringComparer.Ordinal),
            })],
            Destinatari = string.Join('\n', vista.Impostazioni.Destinatari),
            TuttiGliAmministratori = vista.Impostazioni.TuttiGliAmministratori,
            RispostaAutomatica = vista.Impostazioni.RispostaAutomatica,
            GiorniConservazione = vista.Impostazioni.GiorniConservazione.ToString(CultureInfo.InvariantCulture),
        };

    private static Dictionary<string, string> Testi(IReadOnlyDictionary<string, string> testi, IReadOnlyList<LinguaModulo> lingue) =>
        lingue.ToDictionary(l => l.Codice, l => testi.GetValueOrDefault(l.Codice) ?? string.Empty, StringComparer.Ordinal);

    /// <summary>Completa le chiavi di lingua mancanti e toglie quelle sconosciute: la form mostra sempre una riga per lingua del sito.</summary>
    public void AllineaLingue(IReadOnlyList<LinguaModulo> lingue)
    {
        Pulsante = Allinea(Pulsante, lingue);
        Conferma = Allinea(Conferma, lingue);
        foreach (var campo in Campi)
        {
            campo.Etichette = Allinea(campo.Etichette, lingue);
            campo.Aiuti = Allinea(campo.Aiuti, lingue);
            campo.Opzioni = Allinea(campo.Opzioni, lingue);
        }
    }

    private static Dictionary<string, string> Allinea(Dictionary<string, string> testi, IReadOnlyList<LinguaModulo> lingue) =>
        lingue.ToDictionary(l => l.Codice, l => testi.GetValueOrDefault(l.Codice) ?? string.Empty, StringComparer.Ordinal);

    /// <summary>
    /// Converte l'input in dati del servizio. Gli errori di forma (numeri non numeri, tipo sconosciuto, opzioni sfasate) escono qui; le
    /// regole sul contenuto le applica il servizio.
    /// </summary>
    public (DatiModulo? Dati, List<string> Errori) Converti(IReadOnlyList<LinguaModulo> lingue)
    {
        var errori = new List<string>();
        var predefinita = lingue.First(l => l.Predefinita).Codice;

        if (!Enum.TryParse<StatoModulo>(Stato, ignoreCase: false, out var stato) || !Enum.IsDefined(stato))
        {
            errori.Add("Lo stato del modulo non è valido.");
        }

        var campi = new List<CampoModulo>();
        for (var i = 0; i < Campi.Count; i++)
        {
            var campo = Campi[i];
            if (!Enum.TryParse<TipoCampoModulo>(campo.Tipo, ignoreCase: false, out var tipo) || !Enum.IsDefined(tipo))
            {
                errori.Add($"Campo {i + 1}: tipo di campo non valido.");
                continue;
            }

            var lunghezza = CampoModulo.LunghezzaPredefinita(tipo);
            if (tipo is TipoCampoModulo.TestoBreve or TipoCampoModulo.TestoLungo && !string.IsNullOrWhiteSpace(campo.LunghezzaMassima)
                && !int.TryParse(campo.LunghezzaMassima.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out lunghezza))
            {
                errori.Add($"Campo {i + 1}: la lunghezza massima dev'essere un numero.");
            }

            var opzioni = new List<IReadOnlyDictionary<string, string>>();
            if (tipo is TipoCampoModulo.SceltaSingola or TipoCampoModulo.SceltaMultipla)
            {
                var righe = lingue.ToDictionary(
                    l => l.Codice,
                    l => (campo.Opzioni.GetValueOrDefault(l.Codice) ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.Ordinal);
                var righeBase = righe[predefinita];
                if (righe.Any(r => r.Key != predefinita && r.Value.Length > righeBase.Length))
                {
                    errori.Add($"Campo {i + 1}: nelle altre lingue le opzioni non possono essere più numerose di quelle della lingua predefinita.");
                }

                for (var n = 0; n < righeBase.Length; n++)
                {
                    opzioni.Add(righe.Where(r => n < r.Value.Length).ToDictionary(r => r.Key, r => r.Value[n], StringComparer.Ordinal));
                }
            }

            campi.Add(new CampoModulo(
                campo.Id ?? string.Empty,
                tipo,
                campo.Obbligatorio,
                lunghezza,
                campo.Etichette,
                campo.Aiuti,
                opzioni));
        }

        var giorni = Modulo.GiorniConservazionePredefiniti;
        if (!string.IsNullOrWhiteSpace(GiorniConservazione) && !int.TryParse(GiorniConservazione.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out giorni))
        {
            errori.Add("I giorni di conservazione devono essere un numero.");
        }

        if (errori.Count > 0)
        {
            return (null, errori);
        }

        var destinatari = (Destinatari ?? string.Empty).Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (
            new DatiModulo(
                Nome ?? string.Empty,
                stato,
                new DefinizioneModulo(campi, Pulsante, Conferma, PercorsoDopoInvio, UrlInformativa),
                new ImpostazioniModulo(destinatari, TuttiGliAmministratori, RispostaAutomatica, giorni)),
            errori);
    }

    /// <summary>Identificativo da dare a un campo nuovo: il primo <c>cN</c> libero.</summary>
    public string ProssimoIdCampo()
    {
        var usati = Campi.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        for (var n = 1; n < 1000; n++)
        {
            var id = "c" + n.ToString(CultureInfo.InvariantCulture);
            if (!usati.Contains(id))
            {
                return id;
            }
        }

        return "c999";
    }
}

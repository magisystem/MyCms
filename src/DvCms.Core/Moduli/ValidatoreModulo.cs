using System.Text.RegularExpressions;
using DvCms.DTO.Moduli;
using DvCms.Extender;
using DvCms.Models.Moduli;

namespace DvCms.Core.Moduli;

/// <summary>
/// Regole sui moduli di contatto (ADR-0027). I campi hanno tipi e limiti predefiniti dal prodotto: il tenant sceglie e compila, non
/// scrive regole. La struttura (limiti, identificativi, lingue, indirizzi) si controlla sempre; la completezza (almeno un campo, testi
/// nella lingua predefinita, destinatari) solo per un modulo che si pubblica.
/// </summary>
public static partial class ValidatoreModulo
{
    public const int LunghezzaMassimaEtichetta = 200;
    public const int LunghezzaMassimaAiuto = 300;
    public const int LunghezzaMassimaPulsante = 40;
    public const int LunghezzaMassimaConferma = 500;
    public const int LunghezzaMassimaOpzione = 100;
    public const int OpzioniMassime = 30;
    public const int LunghezzaMassimaPercorso = 200;
    public const int LunghezzaMassimaInformativa = 500;

    [GeneratedRegex("^c[0-9]{1,3}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex IdentificativoCampo();

    // Un percorso interno del sito: parte da una sola barra e usa solo caratteri di un percorso (nessuno schema, nessun host, nessun «..»).
    [GeneratedRegex("^/[A-Za-z0-9._~%/-]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PercorsoInterno();

    public static IReadOnlyList<string> Valida(DatiModulo dati, IReadOnlyList<LinguaModulo> lingue)
    {
        ArgumentNullException.ThrowIfNull(dati);
        ArgumentNullException.ThrowIfNull(lingue);

        var errori = new List<string>();
        var predefinita = lingue.FirstOrDefault(l => l.Predefinita)?.Codice ?? (lingue.Count > 0 ? lingue[0].Codice : "it");
        var codici = lingue.Select(l => l.Codice).ToHashSet(StringComparer.Ordinal);
        var pubblica = dati.Stato == StatoModulo.Attivo;

        var nome = dati.Nome?.Trim() ?? string.Empty;
        if (nome.Length == 0)
        {
            errori.Add("Il nome del modulo è obbligatorio.");
        }
        else if (nome.Length > Modulo.LunghezzaMassimaNome)
        {
            errori.Add($"Il nome del modulo supera i {Modulo.LunghezzaMassimaNome} caratteri.");
        }

        ValidaDefinizione(dati.Definizione, codici, predefinita, pubblica, errori);
        ValidaImpostazioni(dati.Impostazioni, dati.Definizione, pubblica, errori);
        return errori;
    }

    private static void ValidaDefinizione(DefinizioneModulo definizione, HashSet<string> codici, string predefinita, bool pubblica, List<string> errori)
    {
        var campi = definizione.Campi;
        if (campi.Count > DefinizioneModulo.NumeroMassimoCampi)
        {
            errori.Add($"Un modulo può avere al massimo {DefinizioneModulo.NumeroMassimoCampi} campi.");
        }

        if (pubblica && campi.Count == 0)
        {
            errori.Add("Per pubblicare il modulo serve almeno un campo.");
        }

        var visti = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < campi.Count; i++)
        {
            ValidaCampo(campi[i], i + 1, visti, codici, predefinita, errori);
        }

        ValidaTesto(definizione.Pulsante, "Il testo del pulsante", LunghezzaMassimaPulsante, codici, predefinita, obbligatorioNellaPredefinita: pubblica, errori);
        if (string.IsNullOrWhiteSpace(definizione.PercorsoDopoInvio))
        {
            ValidaTesto(definizione.Conferma, "Il messaggio di conferma", LunghezzaMassimaConferma, codici, predefinita, obbligatorioNellaPredefinita: pubblica, errori);
        }
        else if (definizione.PercorsoDopoInvio.Length > LunghezzaMassimaPercorso
            || definizione.PercorsoDopoInvio.Contains("//", StringComparison.Ordinal)
            || definizione.PercorsoDopoInvio.Contains("..", StringComparison.Ordinal)
            || !PercorsoInterno().IsMatch(definizione.PercorsoDopoInvio))
        {
            errori.Add("La pagina dopo l'invio dev'essere un percorso del sito che inizia con «/», ad esempio /grazie.");
        }

        var informativa = definizione.UrlInformativa;
        if (!string.IsNullOrWhiteSpace(informativa))
        {
            if (informativa.Length > LunghezzaMassimaInformativa || !(informativa.StartsWith("https://", StringComparison.Ordinal) || (informativa.StartsWith('/') && !informativa.StartsWith("//", StringComparison.Ordinal))) || informativa.Any(char.IsWhiteSpace))
            {
                errori.Add("Il link all'informativa privacy dev'essere un indirizzo https:// o un percorso del sito che inizia con «/».");
            }
        }
        else if (campi.Any(c => c.Tipo == TipoCampoModulo.Consenso))
        {
            errori.Add("Con un campo di consenso serve il link all'informativa privacy.");
        }
    }

    private static void ValidaCampo(CampoModulo campo, int numero, HashSet<string> visti, HashSet<string> codici, string predefinita, List<string> errori)
    {
        var nome = $"Campo {numero}";
        if (!IdentificativoCampo().IsMatch(campo.Id ?? string.Empty) || !visti.Add(campo.Id!))
        {
            errori.Add($"{nome}: identificativo non valido o ripetuto.");
        }

        if (!Enum.IsDefined(campo.Tipo))
        {
            errori.Add($"{nome}: tipo di campo non valido.");
            return;
        }

        ValidaTesto(campo.Etichette, $"{nome}: l'etichetta", LunghezzaMassimaEtichetta, codici, predefinita, obbligatorioNellaPredefinita: true, errori);
        ValidaTesto(campo.Aiuti, $"{nome}: il testo di aiuto", LunghezzaMassimaAiuto, codici, predefinita, obbligatorioNellaPredefinita: false, errori);

        var massimo = CampoModulo.LunghezzaMassimaConsentita(campo.Tipo);
        if (campo.Tipo is TipoCampoModulo.TestoBreve or TipoCampoModulo.TestoLungo)
        {
            if (campo.LunghezzaMassima < 1 || campo.LunghezzaMassima > massimo)
            {
                errori.Add($"{nome}: la lunghezza massima dev'essere fra 1 e {massimo}.");
            }
        }
        else if (campo.LunghezzaMassima != massimo && !campo.EScelta && campo.Tipo != TipoCampoModulo.Consenso)
        {
            errori.Add($"{nome}: la lunghezza di questo tipo di campo è fissa ({massimo}).");
        }

        if (campo.EScelta)
        {
            ValidaOpzioni(campo, nome, codici, predefinita, errori);
        }
        else if (campo.Opzioni.Count > 0)
        {
            errori.Add($"{nome}: solo i campi a scelta hanno opzioni.");
        }
    }

    private static void ValidaOpzioni(CampoModulo campo, string nome, HashSet<string> codici, string predefinita, List<string> errori)
    {
        if (campo.Opzioni.Count < 2 || campo.Opzioni.Count > OpzioniMassime)
        {
            errori.Add($"{nome}: servono da 2 a {OpzioniMassime} opzioni.");
            return;
        }

        for (var i = 0; i < campo.Opzioni.Count; i++)
        {
            ValidaTesto(campo.Opzioni[i], $"{nome}: l'opzione {i + 1}", LunghezzaMassimaOpzione, codici, predefinita, obbligatorioNellaPredefinita: true, errori);
        }
    }

    /// <summary>Un testo per lingua: solo lingue del sito, lunghezza contenuta, nessun carattere di controllo; quello nella lingua predefinita può essere obbligatorio.</summary>
    private static void ValidaTesto(IReadOnlyDictionary<string, string> testi, string nome, int massimo, HashSet<string> codici, string predefinita, bool obbligatorioNellaPredefinita, List<string> errori)
    {
        foreach (var (lingua, testo) in testi)
        {
            if (!codici.Contains(lingua))
            {
                errori.Add($"{nome}: la lingua «{lingua}» non è abilitata nel sito.");
            }
            else if ((testo ?? string.Empty).Length > massimo)
            {
                errori.Add($"{nome} supera i {massimo} caratteri.");
            }
            else if ((testo ?? string.Empty).Any(c => char.IsControl(c) && c is not '\n' and not '\r'))
            {
                errori.Add($"{nome} contiene caratteri non ammessi.");
            }
        }

        if (obbligatorioNellaPredefinita && string.IsNullOrWhiteSpace(testi.GetValueOrDefault(predefinita)))
        {
            errori.Add($"{nome} è obbligatorio nella lingua predefinita ({predefinita}).");
        }
    }

    private static void ValidaImpostazioni(ImpostazioniModulo impostazioni, DefinizioneModulo definizione, bool pubblica, List<string> errori)
    {
        if (impostazioni.GiorniConservazione < Modulo.GiorniConservazioneMinimi || impostazioni.GiorniConservazione > Modulo.GiorniConservazioneMassimi)
        {
            errori.Add($"La conservazione delle richieste dev'essere fra {Modulo.GiorniConservazioneMinimi} e {Modulo.GiorniConservazioneMassimi} giorni.");
        }

        if (impostazioni.Destinatari.Count > ImpostazioniModulo.NumeroMassimoDestinatari)
        {
            errori.Add($"I destinatari possono essere al massimo {ImpostazioniModulo.NumeroMassimoDestinatari}.");
        }

        var normalizzati = new HashSet<string>(StringComparer.Ordinal);
        foreach (var indirizzo in impostazioni.Destinatari)
        {
            if (!NormalizzatoreEmail.TentaNormalizza(indirizzo, out var normalizzato))
            {
                errori.Add($"L'indirizzo «{indirizzo}» non è valido.");
            }
            else if (!normalizzati.Add(normalizzato))
            {
                errori.Add($"L'indirizzo «{indirizzo}» è ripetuto.");
            }
        }

        if (pubblica && !impostazioni.TuttiGliAmministratori && impostazioni.Destinatari.Count == 0)
        {
            errori.Add("Per pubblicare il modulo scegli chi riceve le richieste: almeno un indirizzo oppure tutti gli amministratori.");
        }

        if (impostazioni.RispostaAutomatica && !definizione.Campi.Any(c => c.Tipo == TipoCampoModulo.Email))
        {
            errori.Add("La risposta automatica richiede un campo email nel modulo.");
        }
    }
}

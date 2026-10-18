using System.Globalization;
using System.Text.RegularExpressions;
using DvCms.DTO.Moduli;
using DvCms.Extender;

namespace DvCms.Core.Moduli;

/// <summary>
/// Legge i valori ricevuti da un modulo e li confronta con la definizione corrente (ADR-0027). Conta solo ciò che la definizione prevede:
/// ogni altro campo ricevuto è ignorato (nessun over-posting). Il risultato è autodescrittivo, così la richiesta resta leggibile anche se
/// il modulo cambia in seguito.
/// </summary>
public static partial class ElaboratoreInvioModulo
{
    /// <summary>Prefisso del nome dei campi nella form: <c>campo_c1</c>, <c>campo_c2</c>…</summary>
    public const string PrefissoCampo = "campo_";

    /// <summary>Tetto alla somma dei caratteri ricevuti in un invio.</summary>
    public const int CaratteriMassimiTotali = 20_000;

    [GeneratedRegex(@"^\+?[0-9 ()./-]{5,30}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex FormaTelefono();

    [GeneratedRegex(@"^-?[0-9]{1,15}([.,][0-9]{1,6})?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex FormaNumero();

    public static EsitoElaborazioneInvio Elabora(DefinizioneModulo definizione, string lingua, string linguaPredefinita, IReadOnlyDictionary<string, string[]> ricevuti)
    {
        ArgumentNullException.ThrowIfNull(definizione);
        ArgumentNullException.ThrowIfNull(ricevuti);

        var errori = new Dictionary<string, ErroreCampoModulo>(StringComparer.Ordinal);
        var mostrati = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var contenuto = new List<ValoreInviato>();
        var totale = 0;

        foreach (var campo in definizione.Campi)
        {
            var grezzi = ricevuti.TryGetValue(PrefissoCampo + campo.Id, out var valori) ? valori : [];
            totale += grezzi.Sum(v => (v ?? string.Empty).Length);
            mostrati[campo.Id] = campo.Tipo == TipoCampoModulo.Consenso ? grezzi : [.. grezzi.Select(v => v ?? string.Empty)];

            var etichetta = TestiModulo.Per(campo.Etichette, lingua, linguaPredefinita);
            var esito = ElaboraCampo(campo, grezzi, lingua, linguaPredefinita);
            if (esito.Errore is { } errore)
            {
                errori[campo.Id] = errore;
            }
            else if (esito.Valori.Count > 0)
            {
                contenuto.Add(new ValoreInviato(campo.Id, etichetta, campo.Tipo, esito.Valori));
            }
        }

        if (totale > CaratteriMassimiTotali)
        {
            foreach (var campo in definizione.Campi.Where(c => !errori.ContainsKey(c.Id)).Take(1))
            {
                errori[campo.Id] = ErroreCampoModulo.TroppoLungo;
            }
        }

        return errori.Count > 0 ? new EsitoElaborazioneInvio(errori, mostrati, null) : new EsitoElaborazioneInvio(errori, mostrati, contenuto);
    }

    private static (ErroreCampoModulo? Errore, List<string> Valori) ElaboraCampo(CampoModulo campo, string[] grezzi, string lingua, string linguaPredefinita)
    {
        var vuoto = new List<string>();
        switch (campo.Tipo)
        {
            case TipoCampoModulo.SceltaSingola:
            case TipoCampoModulo.SceltaMultipla:
                return ElaboraScelta(campo, grezzi, lingua, linguaPredefinita);
            case TipoCampoModulo.Consenso:
                if (grezzi.Length == 0 || grezzi.All(string.IsNullOrEmpty))
                {
                    return campo.Obbligatorio ? (ErroreCampoModulo.Obbligatorio, vuoto) : (null, vuoto);
                }

                return grezzi.Length == 1 && grezzi[0] == "si" ? (null, ["si"]) : (ErroreCampoModulo.NonValido, vuoto);
        }

        if (grezzi.Length > 1)
        {
            return (ErroreCampoModulo.NonValido, vuoto);
        }

        var testo = (grezzi.Length == 1 ? grezzi[0] ?? string.Empty : string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (testo.Length == 0)
        {
            return campo.Obbligatorio ? (ErroreCampoModulo.Obbligatorio, vuoto) : (null, vuoto);
        }

        if (testo.Length > campo.LunghezzaMassima)
        {
            return (ErroreCampoModulo.TroppoLungo, vuoto);
        }

        if (testo.Any(c => char.IsControl(c) && !(c == '\n' && campo.Tipo == TipoCampoModulo.TestoLungo)))
        {
            return (ErroreCampoModulo.NonValido, vuoto);
        }

        switch (campo.Tipo)
        {
            case TipoCampoModulo.Email:
                return NormalizzatoreEmail.TentaNormalizza(testo, out var email) ? (null, [email]) : (ErroreCampoModulo.NonValido, vuoto);
            case TipoCampoModulo.Telefono:
                return FormaTelefono().IsMatch(testo) && testo.Count(char.IsDigit) >= 5 ? (null, [testo]) : (ErroreCampoModulo.NonValido, vuoto);
            case TipoCampoModulo.Numero:
                return FormaNumero().IsMatch(testo) ? (null, [testo.Replace(',', '.')]) : (ErroreCampoModulo.NonValido, vuoto);
            case TipoCampoModulo.Data:
                return DateOnly.TryParseExact(testo, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) && data.Year is >= 1900 and <= 2100
                    ? (null, [data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)])
                    : (ErroreCampoModulo.NonValido, vuoto);
            default:
                return (null, [testo]);
        }
    }

    private static (ErroreCampoModulo? Errore, List<string> Valori) ElaboraScelta(CampoModulo campo, string[] grezzi, string lingua, string linguaPredefinita)
    {
        var vuoto = new List<string>();
        var scelti = grezzi.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).ToList();
        if (scelti.Count == 0)
        {
            return campo.Obbligatorio ? (ErroreCampoModulo.Obbligatorio, vuoto) : (null, vuoto);
        }

        if (campo.Tipo == TipoCampoModulo.SceltaSingola && scelti.Count != 1)
        {
            return (ErroreCampoModulo.NonValido, vuoto);
        }

        var etichette = new List<string>();
        foreach (var scelto in scelti)
        {
            if (!scelto.StartsWith('o') || !int.TryParse(scelto.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var posizione)
                || posizione < 1 || posizione > campo.Opzioni.Count || "o" + posizione.ToString(CultureInfo.InvariantCulture) != scelto)
            {
                return (ErroreCampoModulo.NonValido, vuoto);
            }

            etichette.Add(TestiModulo.Per(campo.Opzioni[posizione - 1], lingua, linguaPredefinita));
        }

        return (null, etichette);
    }
}

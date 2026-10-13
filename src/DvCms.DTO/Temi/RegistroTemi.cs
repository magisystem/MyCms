using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DvCms.DTO.Temi;

public enum TipoCampoTema
{
    /// <summary>Un colore esadecimale <c>#RRGGBB</c>.</summary>
    Colore = 1,

    /// <summary>Una riga di testo breve (sempre codificata in uscita).</summary>
    Testo = 2,

    /// <summary>Uno fra i valori ammessi dallo schema.</summary>
    Scelta = 3,
}

/// <summary>Un'impostazione di un tema. Le etichette localizzate stanno nella vista, non qui.</summary>
public sealed record CampoTema(string Nome, TipoCampoTema Tipo, string Predefinito, int LunghezzaMassima = 0, IReadOnlyList<string>? Valori = null);

/// <summary>
/// Un tema in una versione precisa (ADR-0006): presentazione, schema delle impostazioni, e nient'altro. La vista e il foglio di
/// stile del tema sono nel codice, scelti dalla chiave e dalla versione; il tenant non fornisce mai percorsi. Una versione è
/// **immutabile**: per cambiarla si aggiunge una versione nuova, e i siti restano su quella con cui sono configurati.
/// </summary>
public sealed record DefinizioneTema(string Chiave, int Versione, IReadOnlyList<CampoTema> Campi)
{
    /// <summary>Impronta (SHA-256 esadecimale) di ciò che definisce la versione: un test la fissa per ogni versione rilasciata.</summary>
    public string Impronta
    {
        get
        {
            var testo = new StringBuilder().Append(Chiave).Append('|').Append(Versione).Append('|');
            foreach (var campo in Campi)
            {
                testo.Append(campo.Nome).Append(':').Append(campo.Tipo).Append(':').Append(campo.Predefinito).Append(':').Append(campo.LunghezzaMassima).Append(':')
                    .Append(string.Join(',', campo.Valori ?? [])).Append(';');
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo.ToString())));
        }
    }
}

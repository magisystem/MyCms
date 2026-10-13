using System.Security.Cryptography;
using System.Text;

namespace DvCms.DTO.Cms;

public enum TipoCampoModello
{
    /// <summary>Una riga di testo.</summary>
    TestoBreve = 1,

    /// <summary>Più righe di testo.</summary>
    TestoLungo = 2,

    /// <summary>Un indirizzo: percorso del sito (<c>/pagina</c>) oppure indirizzo <c>https://</c> assoluto.</summary>
    Url = 3,
}

/// <summary>Un campo di un modello di pagina. Le etichette localizzate stanno nella vista, non qui.</summary>
public sealed record CampoModello(string Nome, TipoCampoModello Tipo, bool Obbligatorio, int LunghezzaMassima);

/// <summary>
/// Cosa può modificare chi scrive la pagina con questo modello (policy di editing, applicata dal server a ogni salvataggio,
/// qualunque cosa invii il client).
/// </summary>
public sealed record PoliticaEditing(bool ContenutoLiberoAmmesso, int LunghezzaMassimaContenuto);

/// <summary>
/// Un modello di pagina in una versione precisa. Una versione è **immutabile**: per cambiarne i campi o le regole si
/// crea una nuova versione, e le revisioni già scritte continuano a usare (e a disegnarsi con) quella con cui sono nate.
/// </summary>
public sealed record ModelloPagina(
    string Id,
    int Versione,
    IReadOnlyList<CampoModello> Campi,
    PoliticaEditing Politica,
    IReadOnlyList<IReadOnlyList<string>> CampiTuttiONessuno,
    bool Builder = false)
{
    /// <summary>Impronta (SHA-256 esadecimale) di tutto ciò che definisce la versione: un test la fissa per ogni versione rilasciata.</summary>
    public string Impronta
    {
        get
        {
            var testo = new StringBuilder()
                .Append(Id).Append('|').Append(Versione).Append('|')
                .Append(Politica.ContenutoLiberoAmmesso).Append(':').Append(Politica.LunghezzaMassimaContenuto).Append('|');
            foreach (var campo in Campi)
            {
                testo.Append(campo.Nome).Append(':').Append(campo.Tipo).Append(':').Append(campo.Obbligatorio).Append(':').Append(campo.LunghezzaMassima).Append(';');
            }

            testo.Append('|');
            foreach (var gruppo in CampiTuttiONessuno)
            {
                testo.Append(string.Join('+', gruppo)).Append(';');
            }

            if (Builder)
            {
                // I modelli che usano il builder (E-004) hanno un'impronta propria; quella dei modelli a campi non cambia.
                testo.Append("|builder");
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo.ToString())));
        }
    }
}

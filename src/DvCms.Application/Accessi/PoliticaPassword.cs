namespace DvCms.Application.Accessi;

public enum ErrorePassword
{
    Vuota = 1,
    TroppoCorta = 2,
    TroppoLunga = 3,
}

/// <summary>
/// Regole sulle password: lunghezza da 12 a 128 caratteri, nessun obbligo di complessità (si privilegia la lunghezza).
/// La lunghezza massima evita che password enormi diventino un costo di calcolo per il server.
/// </summary>
public static class PoliticaPassword
{
    public const int LunghezzaMinima = 12;
    public const int LunghezzaMassima = 128;

    public static ErrorePassword? Valida(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return ErrorePassword.Vuota;
        }

        if (password.Length < LunghezzaMinima)
        {
            return ErrorePassword.TroppoCorta;
        }

        return password.Length > LunghezzaMassima ? ErrorePassword.TroppoLunga : null;
    }
}

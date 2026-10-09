using System.Net.Mail;

namespace DvCms.Domain.Accessi;

/// <summary>Normalizza gli indirizzi email degli account: spazi eliminati, minuscolo, solo un indirizzo semplice.</summary>
public static class NormalizzatoreEmail
{
    public const int LunghezzaMassima = 254;

    public static bool TentaNormalizza(string? email, out string normalizzata)
    {
        normalizzata = string.Empty;
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var candidata = email.Trim();
        if (candidata.Length > LunghezzaMassima || candidata.Any(carattere => char.IsControl(carattere) || char.IsWhiteSpace(carattere)))
        {
            return false;
        }

        // Accetta solo un indirizzo semplice "locale@dominio": niente nomi visualizzati, commenti o più indirizzi.
        if (!MailAddress.TryCreate(candidata, out var indirizzo)
            || !string.Equals(indirizzo.Address, candidata, StringComparison.Ordinal)
            || candidata.Count(carattere => carattere == '@') != 1)
        {
            return false;
        }

        var dominio = candidata[(candidata.IndexOf('@', StringComparison.Ordinal) + 1)..];
        if (!dominio.Contains('.', StringComparison.Ordinal) && !string.Equals(dominio, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizzata = candidata.ToLowerInvariant();
        return true;
    }
}

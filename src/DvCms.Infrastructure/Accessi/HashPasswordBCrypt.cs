using BCrypt.Net;
using DvCms.Application.Accessi;

namespace DvCms.Infrastructure.Accessi;

/// <summary>
/// Hash BCrypt (libreria BCrypt.Net-Next) nella variante "enhanced": la password è prima ridotta con SHA-384, così
/// non vale il limite di 72 byte di BCrypt. Il costo (work factor) è configurabile; al login un hash con costo
/// inferiore viene rigenerato.
/// </summary>
public sealed class HashPasswordBCrypt : IHashPassword
{
    private readonly int _fattoreLavoro;

    public HashPasswordBCrypt(OpzioniSicurezza opzioni)
    {
        ArgumentNullException.ThrowIfNull(opzioni);
        _fattoreLavoro = opzioni.FattoreLavoroBcrypt;
    }

    public string Calcola(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return BCrypt.Net.BCrypt.EnhancedHashPassword(password, HashType.SHA384, _fattoreLavoro);
    }

    public bool Verifica(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash, HashType.SHA384);
        }
        catch (SaltParseException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool RichiedeAggiornamento(string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(hash, _fattoreLavoro);
        }
        catch (SaltParseException)
        {
            return true;
        }
    }
}

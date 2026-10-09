using System.Security.Cryptography;

namespace DvCms.Application.Accessi;

/// <summary>Genera password temporanee casuali (generatore crittografico, alfabeto senza caratteri ambigui).</summary>
public static class GeneratorePasswordTemporanea
{
    public const int Lunghezza = 20;

    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Genera() => RandomNumberGenerator.GetString(Alfabeto, Lunghezza);
}

using System.Reflection;

namespace DvCms.Domain;

/// <summary>
/// Punto di riferimento per individuare l'assembly del progetto (usato dai test di architettura).
/// </summary>
public static class RiferimentoDominio
{
    public static Assembly Assembly => typeof(RiferimentoDominio).Assembly;
}

using System.Reflection;

namespace DvCms.Models;

/// <summary>
/// Punto di riferimento per individuare l'assembly del progetto (usato dai test di architettura).
/// </summary>
public static class RiferimentoModelli
{
    public static Assembly Assembly => typeof(RiferimentoModelli).Assembly;
}

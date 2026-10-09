using System.Reflection;

namespace DvCms.Application;

/// <summary>
/// Punto di riferimento per individuare l'assembly del progetto (usato dai test di architettura).
/// </summary>
public static class RiferimentoApplicazione
{
    public static Assembly Assembly => typeof(RiferimentoApplicazione).Assembly;
}

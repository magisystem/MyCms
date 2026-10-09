using System.Reflection;

namespace DvCms.Infrastructure;

/// <summary>
/// Punto di riferimento per individuare l'assembly del progetto (usato dai test di architettura).
/// </summary>
public static class RiferimentoInfrastruttura
{
    public static Assembly Assembly => typeof(RiferimentoInfrastruttura).Assembly;
}

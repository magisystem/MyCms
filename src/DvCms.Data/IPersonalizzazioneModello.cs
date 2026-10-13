using Microsoft.EntityFrameworkCore;

namespace DvCms.Data;

/// <summary>
/// Ritocchi del modello che dipendono dal motore di database (per SQL Server, ad esempio le colonne incluse in un indice). Li fornisce il
/// progetto del provider (ADR-0024) e si applicano dopo le configurazioni comuni.
/// </summary>
public interface IPersonalizzazioneModello
{
    void Personalizza(ModelBuilder modelBuilder);
}

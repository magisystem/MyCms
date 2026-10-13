using DvCms.Data;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Persistenza;

/// <summary>Ritocchi del modello specifici di SQL Server.</summary>
public sealed class PersonalizzazioneSqlServer : IPersonalizzazioneModello
{
    public void Personalizza(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Indice di copertura della ricerca nel catalogo: i termini si leggono senza accedere alla tabella.
        modelBuilder.Entity<TermineRicercaProdotto>()
            .HasIndex(termine => new { termine.IdSito, termine.Termine })
            .IncludeProperties(termine => new { termine.IdProdotto, termine.IdLingua, termine.Campo });
    }
}

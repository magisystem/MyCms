using DvCms.Models.Localizzazione;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Data.Configurazioni;

internal sealed class ConfigurazioneLingua : IEntityTypeConfiguration<Lingua>
{
    public void Configure(EntityTypeBuilder<Lingua> entita)
    {
        entita.ToTable("Lingue");
        entita.HasKey(lingua => lingua.Id);
        entita.Property(lingua => lingua.Id).ValueGeneratedNever();
        entita.Property(lingua => lingua.Codice).HasMaxLength(10).IsRequired();
        entita.Property(lingua => lingua.Nome).HasMaxLength(100).IsRequired();
        entita.Property(lingua => lingua.Cultura).HasMaxLength(20).IsRequired();
        entita.HasIndex(lingua => lingua.Codice).IsUnique();

        // Lingue iniziali (ADR-0018): italiano di base e inglese. Altre lingue si aggiungono come righe.
        entita.HasData(
            new Lingua
            {
                Id = Lingua.IdItaliano,
                Codice = Lingua.CodiceItaliano,
                Nome = "Italiano",
                Cultura = "it-IT",
                Predefinita = true,
                Attiva = true,
            },
            new Lingua
            {
                Id = Lingua.IdInglese,
                Codice = Lingua.CodiceInglese,
                Nome = "English",
                Cultura = "en-GB",
                Predefinita = false,
                Attiva = true,
            });
    }
}

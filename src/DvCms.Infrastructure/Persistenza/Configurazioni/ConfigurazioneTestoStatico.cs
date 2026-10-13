using DvCms.Models.Accessi;
using DvCms.Models.Localizzazione;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneTestoStatico : IEntityTypeConfiguration<TestoStatico>
{
    public void Configure(EntityTypeBuilder<TestoStatico> entita)
    {
        entita.ToTable("TestiStatici");
        entita.HasKey(testo => testo.Id);
        entita.Property(testo => testo.Id).ValueGeneratedNever();
        entita.Property(testo => testo.Chiave).HasMaxLength(TestoStatico.LunghezzaMassimaChiave).IsRequired();
        entita.Property(testo => testo.HashChiave).HasColumnType("binary(32)").IsRequired();
        entita.Property(testo => testo.Testo).HasMaxLength(TestoStatico.LunghezzaMassimaTesto).IsRequired();
        entita.HasOne<Lingua>().WithMany().HasForeignKey(testo => testo.IdLingua).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(testo => testo.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(testo => new { testo.IdLingua, testo.HashChiave }).IsUnique();
    }
}

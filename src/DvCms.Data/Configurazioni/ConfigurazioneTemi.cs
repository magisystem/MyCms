using DvCms.Models.Accessi;
using DvCms.Models.Temi;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Data.Configurazioni;

internal sealed class ConfigurazioneImpostazioniTemaSito : IEntityTypeConfiguration<ImpostazioniTemaSito>
{
    public void Configure(EntityTypeBuilder<ImpostazioniTemaSito> entita)
    {
        entita.ToTable("ImpostazioniTemaSito");
        entita.HasKey(impostazioni => impostazioni.Id);
        entita.Property(impostazioni => impostazioni.Id).ValueGeneratedNever();
        entita.Property(impostazioni => impostazioni.ChiaveTema).HasMaxLength(ImpostazioniTemaSito.LunghezzaMassimaChiave).IsRequired();
        entita.Property(impostazioni => impostazioni.ImpostazioniJson).IsRequired();
        entita.Property(impostazioni => impostazioni.TokenConcorrenza).IsRowVersion();

        // Il sito deve essere dello stesso tenant (chiave esterna composta); un solo tema per sito.
        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(impostazioni => new { impostazioni.IdTenant, impostazioni.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(impostazioni => impostazioni.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);

        entita.HasIndex(impostazioni => impostazioni.IdSito).IsUnique();
    }
}

using DvCms.Domain.Accessi;
using DvCms.Domain.Email;
using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Segreti;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneImpostazioniEmailTenant : IEntityTypeConfiguration<ImpostazioniEmailTenant>
{
    public void Configure(EntityTypeBuilder<ImpostazioniEmailTenant> entita)
    {
        entita.ToTable("ImpostazioniEmailTenant");
        entita.HasKey(impostazioni => impostazioni.Id);
        entita.Property(impostazioni => impostazioni.Id).ValueGeneratedNever();
        entita.Property(impostazioni => impostazioni.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(impostazioni => impostazioni.ValoriJson).IsRequired();
        entita.Property(impostazioni => impostazioni.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Tenant>().WithMany().HasForeignKey(impostazioni => impostazioni.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(impostazioni => impostazioni.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(impostazioni => impostazioni.IdTenant).IsUnique();
    }
}

internal sealed class ConfigurazioneChiaveSegreta : IEntityTypeConfiguration<ChiaveSegreta>
{
    public void Configure(EntityTypeBuilder<ChiaveSegreta> entita)
    {
        entita.ToTable("ChiaviSegrete");
        entita.HasKey(chiave => chiave.Id);
        entita.Property(chiave => chiave.Id).ValueGeneratedNever();
        entita.Property(chiave => chiave.NomeAmichevole).HasMaxLength(200).IsRequired();
        entita.Property(chiave => chiave.Xml).IsRequired();
    }
}

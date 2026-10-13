using DvCms.Models.Accessi;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Data.Configurazioni;

internal sealed class ConfigurazioneInvitoAccount : IEntityTypeConfiguration<InvitoAccount>
{
    public void Configure(EntityTypeBuilder<InvitoAccount> entita)
    {
        entita.ToTable("InvitiAccount");
        entita.HasKey(invito => invito.Id);
        entita.Property(invito => invito.Id).ValueGeneratedNever();
        entita.Property(invito => invito.HashToken).HasColumnType("binary(32)").IsRequired();
        entita.Property(invito => invito.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();

        entita.HasOne<Tenant>().WithMany().HasForeignKey(invito => invito.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(invito => invito.IdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(invito => invito.CreatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);

        entita.HasIndex(invito => invito.HashToken).IsUnique();
        entita.HasIndex(invito => new { invito.IdUtenza, invito.Stato });
    }
}

internal sealed class ConfigurazioneConfermaEmailUtenza : IEntityTypeConfiguration<ConfermaEmailUtenza>
{
    public void Configure(EntityTypeBuilder<ConfermaEmailUtenza> entita)
    {
        entita.ToTable("ConfermeEmailUtenza");
        entita.HasKey(conferma => conferma.Id);
        entita.Property(conferma => conferma.Id).ValueGeneratedNever();
        entita.Property(conferma => conferma.HashToken).HasColumnType("binary(32)").IsRequired();
        entita.Property(conferma => conferma.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();

        entita.HasOne<Tenant>().WithMany().HasForeignKey(conferma => conferma.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(conferma => conferma.IdUtenza).OnDelete(DeleteBehavior.Cascade);

        entita.HasIndex(conferma => conferma.HashToken).IsUnique();
        entita.HasIndex(conferma => new { conferma.IdUtenza, conferma.CreatoIlUtc });
    }
}

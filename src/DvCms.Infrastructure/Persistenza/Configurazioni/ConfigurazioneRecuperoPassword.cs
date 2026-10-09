using DvCms.Domain.Accessi;
using DvCms.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneRichiestaRecuperoPassword : IEntityTypeConfiguration<RichiestaRecuperoPassword>
{
    public void Configure(EntityTypeBuilder<RichiestaRecuperoPassword> entita)
    {
        entita.ToTable("RichiesteRecuperoPassword");
        entita.HasKey(richiesta => richiesta.Id);
        entita.Property(richiesta => richiesta.Id).ValueGeneratedNever();
        entita.Property(richiesta => richiesta.EmailNormalizzata).HasMaxLength(NormalizzatoreEmail.LunghezzaMassima).IsRequired();
        entita.Property(richiesta => richiesta.IndirizzoIp).HasMaxLength(45);
        entita.Property(richiesta => richiesta.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(richiesta => richiesta.HashRiferimento).HasColumnType("binary(32)").IsRequired();
        entita.Property(richiesta => richiesta.SaleCodice).HasColumnType("binary(16)").IsRequired();
        entita.Property(richiesta => richiesta.HashCodice).HasColumnType("binary(32)").IsRequired();
        entita.Property(richiesta => richiesta.HashToken).HasColumnType("binary(32)");

        entita.HasOne<Tenant>().WithMany().HasForeignKey(richiesta => richiesta.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(richiesta => richiesta.IdUtenza).OnDelete(DeleteBehavior.Restrict);

        entita.HasIndex(richiesta => richiesta.HashRiferimento);
        entita.HasIndex(richiesta => richiesta.HashToken).HasFilter("[HashToken] IS NOT NULL");
        entita.HasIndex(richiesta => new { richiesta.IdTenant, richiesta.EmailNormalizzata, richiesta.CreatoIlUtc });
        entita.HasIndex(richiesta => new { richiesta.IndirizzoIp, richiesta.CreatoIlUtc });
    }
}

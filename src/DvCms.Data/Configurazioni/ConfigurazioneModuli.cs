using DvCms.Models.Accessi;
using DvCms.Models.Moduli;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Data.Configurazioni;

internal sealed class ConfigurazioneModulo : IEntityTypeConfiguration<Modulo>
{
    public void Configure(EntityTypeBuilder<Modulo> entita)
    {
        entita.ToTable("Moduli");
        entita.HasKey(modulo => modulo.Id);
        entita.Property(modulo => modulo.Id).ValueGeneratedNever();
        entita.Property(modulo => modulo.Nome).HasMaxLength(Modulo.LunghezzaMassimaNome).IsRequired();
        entita.Property(modulo => modulo.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(modulo => modulo.DefinizioneJson).IsRequired();
        entita.Property(modulo => modulo.ImpostazioniJson).IsRequired();
        entita.Property(modulo => modulo.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(modulo => new { modulo.IdTenant, modulo.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(modulo => modulo.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasAlternateKey(modulo => new { modulo.IdTenant, modulo.IdSito, modulo.Id });
        entita.HasIndex(modulo => new { modulo.IdSito, modulo.Nome }).IsUnique();
        entita.ToTable(tabella => tabella.HasCheckConstraint("CK_Moduli_GiorniConservazione", $"[GiorniConservazione] BETWEEN {Modulo.GiorniConservazioneMinimi} AND {Modulo.GiorniConservazioneMassimi}"));
    }
}

internal sealed class ConfigurazioneInvioModulo : IEntityTypeConfiguration<InvioModulo>
{
    public void Configure(EntityTypeBuilder<InvioModulo> entita)
    {
        entita.ToTable("InviiModulo");
        entita.HasKey(invio => invio.Id);
        entita.Property(invio => invio.Id).ValueGeneratedNever();
        entita.Property(invio => invio.CodiceLingua).HasMaxLength(10).IsRequired();
        entita.Property(invio => invio.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(invio => invio.ContenutoJson).IsRequired();
        entita.Property(invio => invio.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Modulo>()
            .WithMany()
            .HasForeignKey(invio => new { invio.IdTenant, invio.IdSito, invio.IdModulo })
            .HasPrincipalKey(modulo => new { modulo.IdTenant, modulo.IdSito, modulo.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(invio => new { invio.IdSito, invio.IdModulo, invio.CreatoIlUtc });
        entita.HasIndex(invio => invio.CreatoIlUtc);
    }
}

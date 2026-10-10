using DvCms.Domain.Accessi;
using DvCms.Domain.Localizzazione;
using DvCms.Domain.Media;
using DvCms.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneRisorsaMedia : IEntityTypeConfiguration<RisorsaMedia>
{
    public void Configure(EntityTypeBuilder<RisorsaMedia> entita)
    {
        entita.ToTable("RisorseMedia");
        entita.HasKey(risorsa => risorsa.Id);
        entita.Property(risorsa => risorsa.Id).ValueGeneratedNever();
        entita.Property(risorsa => risorsa.ChiaveOggetto).HasMaxLength(100).IsRequired();
        entita.Property(risorsa => risorsa.NomeOriginale).HasMaxLength(RisorsaMedia.LunghezzaMassimaNome).IsRequired();
        entita.Property(risorsa => risorsa.TipoContenuto).HasMaxLength(100).IsRequired();
        entita.Property(risorsa => risorsa.HashSha256).HasColumnType("binary(32)").IsRequired();
        entita.Property(risorsa => risorsa.Visibilita).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(risorsa => risorsa.StatoScansione).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(risorsa => risorsa.TokenConcorrenza).IsRowVersion();

        // Il sito deve essere dello stesso tenant (chiave esterna composta).
        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(risorsa => new { risorsa.IdTenant, risorsa.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(risorsa => risorsa.CreatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);

        // Chiave alternativa per le traduzioni: un testo alternativo non può puntare a una risorsa di un altro tenant o sito.
        entita.HasAlternateKey(risorsa => new { risorsa.IdTenant, risorsa.IdSito, risorsa.Id });
        entita.HasIndex(risorsa => risorsa.ChiaveOggetto).IsUnique();
        entita.HasIndex(risorsa => new { risorsa.IdSito, risorsa.CreatoIlUtc });
    }
}

internal sealed class ConfigurazioneTraduzioneRisorsaMedia : IEntityTypeConfiguration<TraduzioneRisorsaMedia>
{
    public void Configure(EntityTypeBuilder<TraduzioneRisorsaMedia> entita)
    {
        entita.ToTable("TraduzioniRisorseMedia");
        entita.HasKey(traduzione => new { traduzione.IdRisorsa, traduzione.IdLingua });
        entita.Property(traduzione => traduzione.TestoAlternativo).HasMaxLength(RisorsaMedia.LunghezzaMassimaTestoAlternativo).IsRequired();

        entita.HasOne<RisorsaMedia>()
            .WithMany()
            .HasForeignKey(traduzione => new { traduzione.IdTenant, traduzione.IdSito, traduzione.IdRisorsa })
            .HasPrincipalKey(risorsa => new { risorsa.IdTenant, risorsa.IdSito, risorsa.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Lingua>().WithMany().HasForeignKey(traduzione => traduzione.IdLingua).OnDelete(DeleteBehavior.Restrict);
    }
}

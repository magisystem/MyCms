using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneTenant : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> entita)
    {
        entita.ToTable("Tenant");
        entita.HasKey(tenant => tenant.Id);
        entita.Property(tenant => tenant.Id).ValueGeneratedNever();
        entita.Property(tenant => tenant.Nome).HasMaxLength(200).IsRequired();
        entita.Property(tenant => tenant.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(tenant => tenant.TokenConcorrenza).IsRowVersion();

        // Il tenant primario è uno solo.
        entita.HasIndex(tenant => tenant.Primario)
            .IsUnique()
            .HasFilter("[Primario] = 1")
            .HasDatabaseName("IX_Tenant_Primario");
    }
}

internal sealed class ConfigurazioneSito : IEntityTypeConfiguration<Sito>
{
    public void Configure(EntityTypeBuilder<Sito> entita)
    {
        // La registrazione degli acquirenti ha senso solo se il negozio è attivo (docs/architecture/domain-model.md).
        entita.ToTable("Siti", tabella => tabella.HasCheckConstraint("CK_Siti_RegistrazioneRichiedeCommercio", "[RegistrazioneAcquirentiAbilitata] = 0 OR [CommercioAttivo] = 1"));
        entita.HasKey(sito => sito.Id);
        entita.Property(sito => sito.Id).ValueGeneratedNever();
        entita.Property(sito => sito.Nome).HasMaxLength(200).IsRequired();
        entita.Property(sito => sito.FusoOrario).HasMaxLength(64).IsRequired();
        entita.Property(sito => sito.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(sito => sito.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Tenant>().WithMany().HasForeignKey(sito => sito.IdTenant).OnDelete(DeleteBehavior.Restrict);

        // Chiave alternativa (IdTenant, Id): permette alle tabelle figlie di avere una chiave esterna composta,
        // così un sito non può essere referenziato da dati di un altro tenant.
        entita.HasAlternateKey(sito => new { sito.IdTenant, sito.Id });
    }
}

internal sealed class ConfigurazioneDominioSito : IEntityTypeConfiguration<DominioSito>
{
    public void Configure(EntityTypeBuilder<DominioSito> entita)
    {
        entita.ToTable(
            "DominiSito",
            tabella => tabella.HasCheckConstraint(
                "CK_DominiSito_HostMinuscolo",
                "[Host] COLLATE Latin1_General_BIN2 = LOWER([Host]) COLLATE Latin1_General_BIN2"));
        entita.HasKey(dominio => dominio.Id);
        entita.Property(dominio => dominio.Id).ValueGeneratedNever();
        entita.Property(dominio => dominio.Host).HasMaxLength(NormalizzatoreHost.LunghezzaMassima).IsRequired();
        entita.Property(dominio => dominio.TokenConcorrenza).IsRowVersion();

        // Un host appartiene a un solo sito in tutta la piattaforma.
        entita.HasIndex(dominio => dominio.Host).IsUnique();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(dominio => new { dominio.IdTenant, dominio.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);

        entita.HasOne<Utenza>().WithMany().HasForeignKey(dominio => dominio.VerificatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurazioneProfiloGuest : IEntityTypeConfiguration<ProfiloGuest>
{
    public void Configure(EntityTypeBuilder<ProfiloGuest> entita)
    {
        entita.ToTable("ProfiliGuest");
        entita.HasKey(profilo => profilo.Id);
        entita.Property(profilo => profilo.Id).ValueGeneratedNever();
        entita.HasOne<Tenant>().WithMany().HasForeignKey(profilo => profilo.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(profilo => profilo.IdTenant).IsUnique();
    }
}

internal sealed class ConfigurazioneLinguaSito : IEntityTypeConfiguration<LinguaSito>
{
    public void Configure(EntityTypeBuilder<LinguaSito> entita)
    {
        entita.ToTable("LingueSito");
        entita.HasKey(lingua => lingua.Id);
        entita.Property(lingua => lingua.Id).ValueGeneratedNever();

        // Il sito deve essere dello stesso tenant (chiave esterna composta).
        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(lingua => new { lingua.IdTenant, lingua.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Models.Localizzazione.Lingua>().WithMany().HasForeignKey(lingua => lingua.IdLingua).OnDelete(DeleteBehavior.Restrict);

        entita.HasIndex(lingua => new { lingua.IdSito, lingua.IdLingua }).IsUnique();

        // Una sola lingua predefinita per sito.
        entita.HasIndex(lingua => lingua.IdSito)
            .IsUnique()
            .HasFilter("[Predefinita] = 1")
            .HasDatabaseName("IX_LingueSito_Predefinita");
    }
}

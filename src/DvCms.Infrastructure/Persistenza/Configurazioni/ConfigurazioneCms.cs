using DvCms.Domain.Accessi;
using DvCms.Domain.Cms;
using DvCms.Domain.Localizzazione;
using DvCms.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazionePagina : IEntityTypeConfiguration<Pagina>
{
    public void Configure(EntityTypeBuilder<Pagina> entita)
    {
        entita.ToTable(
            "Pagine",
            tabella => tabella.HasCheckConstraint(
                "CK_Pagine_SlugMinuscolo",
                "[Slug] COLLATE Latin1_General_BIN2 = LOWER([Slug]) COLLATE Latin1_General_BIN2"));
        entita.HasKey(pagina => pagina.Id);
        entita.Property(pagina => pagina.Id).ValueGeneratedNever();
        entita.Property(pagina => pagina.Slug).HasMaxLength(NormalizzatoreSlug.LunghezzaMassima).IsRequired();
        entita.Property(pagina => pagina.TipoPagina).HasMaxLength(50).IsRequired();
        entita.Property(pagina => pagina.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(pagina => pagina.TokenConcorrenza).IsRowVersion();

        // Il sito deve essere dello stesso tenant (chiave esterna composta).
        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(pagina => new { pagina.IdTenant, pagina.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Lingua>().WithMany().HasForeignKey(pagina => pagina.IdLingua).OnDelete(DeleteBehavior.Restrict);

        // Chiave alternativa per le revisioni: una revisione non può puntare a una pagina di un altro tenant o sito.
        entita.HasAlternateKey(pagina => new { pagina.IdTenant, pagina.IdSito, pagina.Id });

        // Le due revisioni puntate devono appartenere a questa stessa pagina. Nessuna cascata (ciclo pagina-revisione).
        entita.HasOne<RevisionePagina>()
            .WithMany()
            .HasForeignKey(pagina => new { pagina.Id, pagina.IdUltimaRevisioneBozza })
            .HasPrincipalKey(revisione => new { revisione.IdPagina, revisione.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<RevisionePagina>()
            .WithMany()
            .HasForeignKey(pagina => new { pagina.Id, pagina.IdRevisionePubblicata })
            .HasPrincipalKey(revisione => new { revisione.IdPagina, revisione.Id })
            .OnDelete(DeleteBehavior.Restrict);

        entita.HasIndex(pagina => new { pagina.IdSito, pagina.IdLingua, pagina.Slug }).IsUnique();
        entita.HasIndex(pagina => new { pagina.IdSito, pagina.IdLingua })
            .IsUnique()
            .HasFilter("[PaginaIniziale] = 1")
            .HasDatabaseName("IX_Pagine_PaginaIniziale");
    }
}

internal sealed class ConfigurazioneRevisionePagina : IEntityTypeConfiguration<RevisionePagina>
{
    public void Configure(EntityTypeBuilder<RevisionePagina> entita)
    {
        entita.ToTable("RevisioniPagina");
        entita.HasKey(revisione => revisione.Id);
        entita.Property(revisione => revisione.Id).ValueGeneratedNever();
        entita.Property(revisione => revisione.Titolo).HasMaxLength(RevisionePagina.LunghezzaMassimaTitolo).IsRequired();
        entita.Property(revisione => revisione.Contenuto).IsRequired();
        entita.Property(revisione => revisione.NotaRevisione).HasMaxLength(RevisionePagina.LunghezzaMassimaNota);

        entita.HasOne<Pagina>()
            .WithMany()
            .HasForeignKey(revisione => new { revisione.IdTenant, revisione.IdSito, revisione.IdPagina })
            .HasPrincipalKey(pagina => new { pagina.IdTenant, pagina.IdSito, pagina.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(revisione => revisione.CreatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);

        entita.HasAlternateKey(revisione => new { revisione.IdPagina, revisione.Id });
        entita.HasIndex(revisione => new { revisione.IdPagina, revisione.NumeroRevisione }).IsUnique();
    }
}

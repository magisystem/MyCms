using DvCms.Domain.Localizzazione;
using DvCms.Domain.Negozio;
using DvCms.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneProdotto : IEntityTypeConfiguration<Prodotto>
{
    public void Configure(EntityTypeBuilder<Prodotto> entita)
    {
        entita.ToTable("Prodotti", tabella => tabella.HasCheckConstraint("CK_Prodotti_AliquotaIva", "[AliquotaIva] IN (0, 4, 5, 10, 22)"));
        entita.HasKey(prodotto => prodotto.Id);
        entita.Property(prodotto => prodotto.Id).ValueGeneratedNever();
        entita.Property(prodotto => prodotto.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(prodotto => prodotto.AliquotaIva).HasPrecision(5, 2);
        entita.Property(prodotto => prodotto.TokenConcorrenza).IsRowVersion();

        // Il sito deve essere dello stesso tenant (chiave esterna composta).
        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(prodotto => new { prodotto.IdTenant, prodotto.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Chiave alternativa per le tabelle figlie: nulla può puntare a un prodotto di un altro tenant o sito.
        entita.HasAlternateKey(prodotto => new { prodotto.IdTenant, prodotto.IdSito, prodotto.Id });
        entita.HasIndex(prodotto => new { prodotto.IdSito, prodotto.Stato });
    }
}

internal sealed class ConfigurazioneVarianteProdotto : IEntityTypeConfiguration<VarianteProdotto>
{
    public void Configure(EntityTypeBuilder<VarianteProdotto> entita)
    {
        entita.ToTable("VariantiProdotto", tabella =>
        {
            tabella.HasCheckConstraint("CK_VariantiProdotto_Prezzo", "[PrezzoLordo] > 0");
            tabella.HasCheckConstraint("CK_VariantiProdotto_Valuta", "[Valuta] = 'EUR'");
        });
        entita.HasKey(variante => variante.Id);
        entita.Property(variante => variante.Id).ValueGeneratedNever();
        entita.Property(variante => variante.Sku).HasMaxLength(50).IsRequired();
        entita.Property(variante => variante.PrezzoLordo).HasPrecision(18, 2);
        entita.Property(variante => variante.Valuta).HasMaxLength(3).IsFixedLength().IsRequired();

        entita.HasOne<Prodotto>()
            .WithMany()
            .HasForeignKey(variante => new { variante.IdTenant, variante.IdSito, variante.IdProdotto })
            .HasPrincipalKey(prodotto => new { prodotto.IdTenant, prodotto.IdSito, prodotto.Id })
            .OnDelete(DeleteBehavior.Restrict);

        entita.HasAlternateKey(variante => new { variante.IdTenant, variante.IdSito, variante.Id });
        // Il codice articolo è unico nel sito.
        entita.HasIndex(variante => new { variante.IdSito, variante.Sku }).IsUnique();
        entita.HasIndex(variante => variante.IdProdotto);
    }
}

internal sealed class ConfigurazioneTraduzioneProdotto : IEntityTypeConfiguration<TraduzioneProdotto>
{
    public void Configure(EntityTypeBuilder<TraduzioneProdotto> entita)
    {
        entita.ToTable("TraduzioniProdotto");
        entita.HasKey(traduzione => new { traduzione.IdProdotto, traduzione.IdLingua });
        entita.Property(traduzione => traduzione.Nome).HasMaxLength(Prodotto.LunghezzaMassimaNome).IsRequired();
        entita.Property(traduzione => traduzione.Descrizione).HasMaxLength(Prodotto.LunghezzaMassimaDescrizione).IsRequired();

        entita.HasOne<Prodotto>()
            .WithMany()
            .HasForeignKey(traduzione => new { traduzione.IdTenant, traduzione.IdSito, traduzione.IdProdotto })
            .HasPrincipalKey(prodotto => new { prodotto.IdTenant, prodotto.IdSito, prodotto.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Lingua>().WithMany().HasForeignKey(traduzione => traduzione.IdLingua).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurazioneTraduzioneVarianteProdotto : IEntityTypeConfiguration<TraduzioneVarianteProdotto>
{
    public void Configure(EntityTypeBuilder<TraduzioneVarianteProdotto> entita)
    {
        entita.ToTable("TraduzioniVarianteProdotto");
        entita.HasKey(traduzione => new { traduzione.IdVariante, traduzione.IdLingua });
        entita.Property(traduzione => traduzione.Etichetta).HasMaxLength(VarianteProdotto.LunghezzaMassimaEtichetta).IsRequired();

        entita.HasOne<VarianteProdotto>()
            .WithMany()
            .HasForeignKey(traduzione => new { traduzione.IdTenant, traduzione.IdSito, traduzione.IdVariante })
            .HasPrincipalKey(variante => new { variante.IdTenant, variante.IdSito, variante.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Lingua>().WithMany().HasForeignKey(traduzione => traduzione.IdLingua).OnDelete(DeleteBehavior.Restrict);
    }
}

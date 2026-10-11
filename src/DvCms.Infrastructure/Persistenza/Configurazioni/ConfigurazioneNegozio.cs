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

internal sealed class ConfigurazioneCarrello : IEntityTypeConfiguration<Carrello>
{
    public void Configure(EntityTypeBuilder<Carrello> entita)
    {
        entita.ToTable("Carrelli");
        entita.HasKey(carrello => carrello.Id);
        entita.Property(carrello => carrello.Id).ValueGeneratedNever();
        entita.Property(carrello => carrello.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(carrello => new { carrello.IdTenant, carrello.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Domain.Accessi.Utenza>().WithMany().HasForeignKey(carrello => carrello.IdUtenza).OnDelete(DeleteBehavior.Restrict);

        entita.HasAlternateKey(carrello => new { carrello.IdTenant, carrello.IdSito, carrello.Id });
        // Un solo carrello per account e per sito.
        entita.HasIndex(carrello => new { carrello.IdSito, carrello.IdUtenza }).IsUnique();
    }
}

internal sealed class ConfigurazioneRigaCarrello : IEntityTypeConfiguration<RigaCarrello>
{
    public void Configure(EntityTypeBuilder<RigaCarrello> entita)
    {
        entita.ToTable("RigheCarrello", tabella => tabella.HasCheckConstraint("CK_RigheCarrello_Quantita", "[Quantita] BETWEEN 1 AND 99"));
        entita.HasKey(riga => new { riga.IdCarrello, riga.IdVariante });

        entita.HasOne<Carrello>()
            .WithMany()
            .HasForeignKey(riga => new { riga.IdTenant, riga.IdSito, riga.IdCarrello })
            .HasPrincipalKey(carrello => new { carrello.IdTenant, carrello.IdSito, carrello.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<VarianteProdotto>()
            .WithMany()
            .HasForeignKey(riga => new { riga.IdTenant, riga.IdSito, riga.IdVariante })
            .HasPrincipalKey(variante => new { variante.IdTenant, variante.IdSito, variante.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurazioneMetodoSpedizione : IEntityTypeConfiguration<MetodoSpedizione>
{
    public void Configure(EntityTypeBuilder<MetodoSpedizione> entita)
    {
        entita.ToTable("MetodiSpedizione", tabella =>
        {
            tabella.HasCheckConstraint("CK_MetodiSpedizione_TipoEArea", "([Tipo] = 'Ritiro' AND [Area] IS NULL AND [TariffaLorda] = 0) OR ([Tipo] = 'Zona' AND [Area] IS NOT NULL AND [TariffaLorda] >= 0)");
        });
        entita.HasKey(metodo => metodo.Id);
        entita.Property(metodo => metodo.Id).ValueGeneratedNever();
        entita.Property(metodo => metodo.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(metodo => metodo.Area).HasConversion<string>().HasMaxLength(20);
        entita.Property(metodo => metodo.Nome).HasMaxLength(MetodoSpedizione.LunghezzaMassimaNome).IsRequired();
        entita.Property(metodo => metodo.TariffaLorda).HasPrecision(18, 2);

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(metodo => new { metodo.IdTenant, metodo.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(metodo => new { metodo.IdSito, metodo.Posizione });
    }
}

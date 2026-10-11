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

internal sealed class ConfigurazioneOrdine : IEntityTypeConfiguration<Ordine>
{
    public void Configure(EntityTypeBuilder<Ordine> entita)
    {
        entita.ToTable("Ordini", tabella =>
        {
            tabella.HasCheckConstraint("CK_Ordini_Totali", "[TotaleLordo] = [TotaleImponibile] + [TotaleIva] AND [SpedizioneLorda] = [SpedizioneImponibile] + [SpedizioneIva] AND [TotaleLordo] >= [SpedizioneLorda]");
            tabella.HasCheckConstraint("CK_Ordini_Consegna", "([TipoSpedizione] = 'Ritiro' AND [Paese] IS NULL AND [AreaSpedizione] IS NULL AND [SpedizioneLorda] = 0) OR ([TipoSpedizione] = 'Zona' AND [Paese] IS NOT NULL AND [AreaSpedizione] IS NOT NULL)");
        });
        entita.HasKey(ordine => ordine.Id);
        entita.Property(ordine => ordine.Id).ValueGeneratedNever();
        entita.Property(ordine => ordine.TokenConcorrenza).IsRowVersion();
        entita.Property(ordine => ordine.Stato).HasConversion<string>().HasMaxLength(30).IsRequired();
        entita.Property(ordine => ordine.Valuta).HasMaxLength(3).IsFixedLength().IsRequired();
        entita.Property(ordine => ordine.TotaleLordo).HasPrecision(18, 2);
        entita.Property(ordine => ordine.TotaleImponibile).HasPrecision(18, 2);
        entita.Property(ordine => ordine.TotaleIva).HasPrecision(18, 2);
        entita.Property(ordine => ordine.TipoSpedizione).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(ordine => ordine.AreaSpedizione).HasConversion<string>().HasMaxLength(20);
        entita.Property(ordine => ordine.NomeSpedizione).HasMaxLength(MetodoSpedizione.LunghezzaMassimaNome).IsRequired();
        entita.Property(ordine => ordine.SpedizioneLorda).HasPrecision(18, 2);
        entita.Property(ordine => ordine.SpedizioneImponibile).HasPrecision(18, 2);
        entita.Property(ordine => ordine.SpedizioneIva).HasPrecision(18, 2);
        entita.Property(ordine => ordine.EmailAcquirente).HasMaxLength(320).IsRequired();
        entita.Property(ordine => ordine.NomeAcquirente).HasMaxLength(200).IsRequired();
        entita.Property(ordine => ordine.Destinatario).HasMaxLength(Ordine.LunghezzaMassimaDestinatario);
        entita.Property(ordine => ordine.IndirizzoRiga1).HasMaxLength(Ordine.LunghezzaMassimaIndirizzo);
        entita.Property(ordine => ordine.IndirizzoRiga2).HasMaxLength(Ordine.LunghezzaMassimaIndirizzo);
        entita.Property(ordine => ordine.Cap).HasMaxLength(Ordine.LunghezzaMassimaCap);
        entita.Property(ordine => ordine.Citta).HasMaxLength(Ordine.LunghezzaMassimaCitta);
        entita.Property(ordine => ordine.Paese).HasMaxLength(2).IsFixedLength();
        entita.Property(ordine => ordine.PrenotazioneScadeIlUtc);

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(ordine => new { ordine.IdTenant, ordine.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Domain.Accessi.Utenza>().WithMany().HasForeignKey(ordine => ordine.IdUtenza).OnDelete(DeleteBehavior.Restrict);

        entita.HasAlternateKey(ordine => new { ordine.IdTenant, ordine.IdSito, ordine.Id });
        entita.HasIndex(ordine => new { ordine.IdSito, ordine.Numero }).IsUnique();
        entita.HasIndex(ordine => new { ordine.IdSito, ordine.IdUtenza, ordine.CreatoIlUtc });
    }
}

internal sealed class ConfigurazioneRigaOrdine : IEntityTypeConfiguration<RigaOrdine>
{
    public void Configure(EntityTypeBuilder<RigaOrdine> entita)
    {
        entita.ToTable("RigheOrdine", tabella =>
        {
            tabella.HasCheckConstraint("CK_RigheOrdine_Quantita", "[Quantita] BETWEEN 1 AND 99");
            tabella.HasCheckConstraint("CK_RigheOrdine_Importi", "[Lordo] = [Imponibile] + [Iva] AND [Lordo] = [PrezzoUnitarioLordo] * [Quantita]");
        });
        entita.HasKey(riga => riga.Id);
        entita.Property(riga => riga.Id).ValueGeneratedNever();
        entita.Property(riga => riga.Sku).HasMaxLength(50).IsRequired();
        entita.Property(riga => riga.NomeProdotto).HasMaxLength(200).IsRequired();
        entita.Property(riga => riga.EtichettaVariante).HasMaxLength(200);
        entita.Property(riga => riga.PrezzoUnitarioLordo).HasPrecision(18, 2);
        entita.Property(riga => riga.AliquotaIva).HasPrecision(5, 2);
        entita.Property(riga => riga.Lordo).HasPrecision(18, 2);
        entita.Property(riga => riga.Imponibile).HasPrecision(18, 2);
        entita.Property(riga => riga.Iva).HasPrecision(18, 2);

        entita.HasOne<Ordine>()
            .WithMany()
            .HasForeignKey(riga => new { riga.IdTenant, riga.IdSito, riga.IdOrdine })
            .HasPrincipalKey(ordine => new { ordine.IdTenant, ordine.IdSito, ordine.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(riga => new { riga.IdOrdine, riga.Posizione }).IsUnique();
    }
}

internal sealed class ConfigurazioneGiacenzaVariante : IEntityTypeConfiguration<GiacenzaVariante>
{
    public void Configure(EntityTypeBuilder<GiacenzaVariante> entita)
    {
        entita.ToTable("GiacenzeVarianti", tabella => tabella.HasCheckConstraint("CK_GiacenzeVarianti_Quantita", "[Disponibile] >= 0 AND [Riservata] >= 0 AND [Riservata] <= [Disponibile] AND [Disponibile] <= 1000000"));
        entita.HasKey(giacenza => giacenza.IdVariante);
        entita.Property(giacenza => giacenza.IdVariante).ValueGeneratedNever();
        entita.Property(giacenza => giacenza.TokenConcorrenza).IsRowVersion();

        entita.HasOne<VarianteProdotto>()
            .WithMany()
            .HasForeignKey(giacenza => new { giacenza.IdTenant, giacenza.IdSito, giacenza.IdVariante })
            .HasPrincipalKey(variante => new { variante.IdTenant, variante.IdSito, variante.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurazionePrenotazioneGiacenza : IEntityTypeConfiguration<PrenotazioneGiacenza>
{
    public void Configure(EntityTypeBuilder<PrenotazioneGiacenza> entita)
    {
        entita.ToTable("PrenotazioniGiacenza", tabella => tabella.HasCheckConstraint("CK_PrenotazioniGiacenza_Quantita", "[Quantita] BETWEEN 1 AND 99"));
        entita.HasKey(prenotazione => prenotazione.Id);
        entita.Property(prenotazione => prenotazione.Id).ValueGeneratedNever();
        entita.Property(prenotazione => prenotazione.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();

        entita.HasOne<Ordine>()
            .WithMany()
            .HasForeignKey(prenotazione => new { prenotazione.IdTenant, prenotazione.IdSito, prenotazione.IdOrdine })
            .HasPrincipalKey(ordine => new { ordine.IdTenant, ordine.IdSito, ordine.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(prenotazione => new { prenotazione.IdOrdine, prenotazione.IdVariante }).IsUnique();
        // Cerca le prenotazioni da far scadere senza scorrere quelle concluse.
        entita.HasIndex(prenotazione => prenotazione.ScadeIlUtc).HasFilter("[Stato] = 'Attiva'");
    }
}

internal sealed class ConfigurazioneMovimentoGiacenza : IEntityTypeConfiguration<MovimentoGiacenza>
{
    public void Configure(EntityTypeBuilder<MovimentoGiacenza> entita)
    {
        entita.ToTable("MovimentiGiacenza");
        entita.HasKey(movimento => movimento.Id);
        entita.Property(movimento => movimento.Id).ValueGeneratedNever();
        entita.Property(movimento => movimento.Motivo).HasConversion<string>().HasMaxLength(20).IsRequired();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(movimento => new { movimento.IdTenant, movimento.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(movimento => new { movimento.IdVariante, movimento.CreatoIlUtc });
    }
}

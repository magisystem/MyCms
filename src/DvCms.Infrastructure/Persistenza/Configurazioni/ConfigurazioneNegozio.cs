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

internal sealed class ConfigurazioneCategoria : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> entita)
    {
        entita.ToTable("Categorie");
        entita.HasKey(categoria => categoria.Id);
        entita.Property(categoria => categoria.Id).ValueGeneratedNever();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(categoria => new { categoria.IdTenant, categoria.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasAlternateKey(categoria => new { categoria.IdTenant, categoria.IdSito, categoria.Id });
        entita.HasIndex(categoria => new { categoria.IdSito, categoria.Posizione });
    }
}

internal sealed class ConfigurazioneTraduzioneCategoria : IEntityTypeConfiguration<TraduzioneCategoria>
{
    public void Configure(EntityTypeBuilder<TraduzioneCategoria> entita)
    {
        entita.ToTable("TraduzioniCategoria");
        entita.HasKey(traduzione => new { traduzione.IdCategoria, traduzione.IdLingua });
        entita.Property(traduzione => traduzione.Nome).HasMaxLength(Categoria.LunghezzaMassimaNome).IsRequired();

        entita.HasOne<Categoria>()
            .WithMany()
            .HasForeignKey(traduzione => new { traduzione.IdTenant, traduzione.IdSito, traduzione.IdCategoria })
            .HasPrincipalKey(categoria => new { categoria.IdTenant, categoria.IdSito, categoria.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Lingua>().WithMany().HasForeignKey(traduzione => traduzione.IdLingua).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurazioneProdottoCategoria : IEntityTypeConfiguration<ProdottoCategoria>
{
    public void Configure(EntityTypeBuilder<ProdottoCategoria> entita)
    {
        entita.ToTable("ProdottiCategoria");
        entita.HasKey(appartenenza => new { appartenenza.IdProdotto, appartenenza.IdCategoria });

        entita.HasOne<Prodotto>()
            .WithMany()
            .HasForeignKey(appartenenza => new { appartenenza.IdTenant, appartenenza.IdSito, appartenenza.IdProdotto })
            .HasPrincipalKey(prodotto => new { prodotto.IdTenant, prodotto.IdSito, prodotto.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Categoria>()
            .WithMany()
            .HasForeignKey(appartenenza => new { appartenenza.IdTenant, appartenenza.IdSito, appartenenza.IdCategoria })
            .HasPrincipalKey(categoria => new { categoria.IdTenant, categoria.IdSito, categoria.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(appartenenza => appartenenza.IdCategoria);
    }
}

internal sealed class ConfigurazioneImmagineProdotto : IEntityTypeConfiguration<ImmagineProdotto>
{
    public void Configure(EntityTypeBuilder<ImmagineProdotto> entita)
    {
        entita.ToTable("ImmaginiProdotto");
        entita.HasKey(immagine => new { immagine.IdProdotto, immagine.IdRisorsa });

        entita.HasOne<Prodotto>()
            .WithMany()
            .HasForeignKey(immagine => new { immagine.IdTenant, immagine.IdSito, immagine.IdProdotto })
            .HasPrincipalKey(prodotto => new { prodotto.IdTenant, prodotto.IdSito, prodotto.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(immagine => new { immagine.IdProdotto, immagine.Posizione });
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

internal sealed class ConfigurazioneTermineRicercaProdotto : IEntityTypeConfiguration<TermineRicercaProdotto>
{
    public void Configure(EntityTypeBuilder<TermineRicercaProdotto> entita)
    {
        entita.ToTable("TerminiRicercaProdotto");
        entita.HasKey(termine => new { termine.IdProdotto, termine.IdLingua, termine.Campo, termine.Termine });
        entita.Property(termine => termine.Campo).HasConversion<byte>();

        // Confronto binario: i termini sono già normalizzati e così la ricerca per prefisso usa l'indice.
        entita.Property(termine => termine.Termine).HasMaxLength(AnalizzatoreRicerca.LunghezzaMassima).UseCollation("Latin1_General_100_BIN2").IsRequired();

        // Nessun vincolo sulla lingua: i codici articolo usano la lingua neutra (Guid vuoto).
        entita.HasOne<Prodotto>()
            .WithMany()
            .HasForeignKey(termine => new { termine.IdTenant, termine.IdSito, termine.IdProdotto })
            .HasPrincipalKey(prodotto => new { prodotto.IdTenant, prodotto.IdSito, prodotto.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(termine => new { termine.IdSito, termine.Termine }).IncludeProperties(termine => new { termine.IdProdotto, termine.IdLingua, termine.Campo });
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
            tabella.HasCheckConstraint("CK_Ordini_Totali", "[TotaleLordo] = [TotaleImponibile] + [TotaleIva] AND [SpedizioneLorda] = [SpedizioneImponibile] + [SpedizioneIva] AND [CommissioneLorda] = [CommissioneImponibile] + [CommissioneIva] AND [TotaleLordo] >= [SpedizioneLorda] + [CommissioneLorda]");
            tabella.HasCheckConstraint("CK_Ordini_Commissione", "[CommissioneLorda] = 0 OR [ModalitaPagamento] = 'Contrassegno'");
            tabella.HasCheckConstraint("CK_Ordini_Rimborsato", "[TotaleRimborsato] >= 0 AND [TotaleRimborsato] <= [TotaleLordo]");
            tabella.HasCheckConstraint("CK_Ordini_Consegna", "([TipoSpedizione] = 'Ritiro' AND [Paese] IS NULL AND [AreaSpedizione] IS NULL AND [SpedizioneLorda] = 0) OR ([TipoSpedizione] = 'Zona' AND [Paese] IS NOT NULL AND [AreaSpedizione] IS NOT NULL)");
        });
        entita.HasKey(ordine => ordine.Id);
        entita.Property(ordine => ordine.Id).ValueGeneratedNever();
        entita.Property(ordine => ordine.TokenConcorrenza).IsRowVersion();
        entita.Property(ordine => ordine.Stato).HasConversion<string>().HasMaxLength(30).IsRequired();
        entita.Property(ordine => ordine.ModalitaPagamento).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(ordine => ordine.CodiceLingua).HasMaxLength(10).IsRequired();
        entita.Property(ordine => ordine.Valuta).HasMaxLength(3).IsFixedLength().IsRequired();
        entita.Property(ordine => ordine.TotaleLordo).HasPrecision(18, 2);
        entita.Property(ordine => ordine.TotaleImponibile).HasPrecision(18, 2);
        entita.Property(ordine => ordine.TotaleIva).HasPrecision(18, 2);
        entita.Property(ordine => ordine.TotaleRimborsato).HasPrecision(18, 2);
        entita.Property(ordine => ordine.TipoSpedizione).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(ordine => ordine.AreaSpedizione).HasConversion<string>().HasMaxLength(20);
        entita.Property(ordine => ordine.NomeSpedizione).HasMaxLength(MetodoSpedizione.LunghezzaMassimaNome).IsRequired();
        entita.Property(ordine => ordine.SpedizioneLorda).HasPrecision(18, 2);
        entita.Property(ordine => ordine.SpedizioneImponibile).HasPrecision(18, 2);
        entita.Property(ordine => ordine.SpedizioneIva).HasPrecision(18, 2);
        entita.Property(ordine => ordine.CommissioneLorda).HasPrecision(18, 2);
        entita.Property(ordine => ordine.CommissioneImponibile).HasPrecision(18, 2);
        entita.Property(ordine => ordine.CommissioneIva).HasPrecision(18, 2);
        entita.Property(ordine => ordine.EmailAcquirente).HasMaxLength(320).IsRequired();
        entita.Property(ordine => ordine.NomeAcquirente).HasMaxLength(200).IsRequired();
        entita.Property(ordine => ordine.Destinatario).HasMaxLength(Ordine.LunghezzaMassimaDestinatario);
        entita.Property(ordine => ordine.IndirizzoRiga1).HasMaxLength(Ordine.LunghezzaMassimaIndirizzo);
        entita.Property(ordine => ordine.IndirizzoRiga2).HasMaxLength(Ordine.LunghezzaMassimaIndirizzo);
        entita.Property(ordine => ordine.Cap).HasMaxLength(Ordine.LunghezzaMassimaCap);
        entita.Property(ordine => ordine.Citta).HasMaxLength(Ordine.LunghezzaMassimaCitta);
        entita.Property(ordine => ordine.Paese).HasMaxLength(2).IsFixedLength();
        entita.Property(ordine => ordine.PrenotazioneScadeIlUtc);
        entita.Property(ordine => ordine.AnonimizzatoIlUtc);

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(ordine => new { ordine.IdTenant, ordine.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Domain.Accessi.Utenza>().WithMany().HasForeignKey(ordine => ordine.IdUtenza).OnDelete(DeleteBehavior.Restrict);

        entita.HasAlternateKey(ordine => new { ordine.IdTenant, ordine.IdSito, ordine.Id });
        entita.HasIndex(ordine => new { ordine.IdSito, ordine.Numero }).IsUnique();
        entita.HasIndex(ordine => new { ordine.IdSito, ordine.IdUtenza, ordine.CreatoIlUtc });
        entita.HasIndex(ordine => ordine.AggiornatoIlUtc).HasFilter("[AnonimizzatoIlUtc] IS NULL");
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

internal sealed class ConfigurazioneImpostazioniPagamentoSito : IEntityTypeConfiguration<ImpostazioniPagamentoSito>
{
    public void Configure(EntityTypeBuilder<ImpostazioniPagamentoSito> entita)
    {
        entita.ToTable("ImpostazioniPagamentoSiti");
        entita.HasKey(impostazioni => impostazioni.Id);
        entita.Property(impostazioni => impostazioni.Id).ValueGeneratedNever();
        entita.Property(impostazioni => impostazioni.Ambiente).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(impostazioni => impostazioni.IdClient).HasMaxLength(ImpostazioniPagamentoSito.LunghezzaMassimaIdClient).IsRequired();
        entita.Property(impostazioni => impostazioni.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(impostazioni => new { impostazioni.IdTenant, impostazioni.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Domain.Accessi.Utenza>().WithMany().HasForeignKey(impostazioni => impostazioni.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(impostazioni => impostazioni.IdSito).IsUnique();
    }
}

internal sealed class ConfigurazioneImpostazioniNexiSito : IEntityTypeConfiguration<ImpostazioniNexiSito>
{
    public void Configure(EntityTypeBuilder<ImpostazioniNexiSito> entita)
    {
        entita.ToTable("ImpostazioniNexiSiti");
        entita.HasKey(impostazioni => impostazioni.Id);
        entita.Property(impostazioni => impostazioni.Id).ValueGeneratedNever();
        entita.Property(impostazioni => impostazioni.Ambiente).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(impostazioni => impostazioni.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(impostazioni => new { impostazioni.IdTenant, impostazioni.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Domain.Accessi.Utenza>().WithMany().HasForeignKey(impostazioni => impostazioni.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(impostazioni => impostazioni.IdSito).IsUnique();
    }
}

internal sealed class ConfigurazioneImpostazioniPagamentoOfflineSito : IEntityTypeConfiguration<ImpostazioniPagamentoOfflineSito>
{
    public void Configure(EntityTypeBuilder<ImpostazioniPagamentoOfflineSito> entita)
    {
        entita.ToTable("ImpostazioniPagamentoOfflineSiti", tabella =>
        {
            tabella.HasCheckConstraint("CK_ImpostazioniPagamentoOfflineSiti_Giorni", "[GiorniPerPagare] BETWEEN 1 AND 60");
            tabella.HasCheckConstraint("CK_ImpostazioniPagamentoOfflineSiti_Commissione", "[CommissioneFissa] BETWEEN 0 AND 100000 AND [CommissionePercentuale] BETWEEN 0 AND 100");
            tabella.HasCheckConstraint("CK_ImpostazioniPagamentoOfflineSiti_Limiti", "([OrdineMinimo] IS NULL OR [OrdineMinimo] >= 0) AND ([OrdineMassimo] IS NULL OR [OrdineMassimo] >= 0) AND ([OrdineMinimo] IS NULL OR [OrdineMassimo] IS NULL OR [OrdineMinimo] <= [OrdineMassimo]) AND ([CommissioneGratisSopra] IS NULL OR [CommissioneGratisSopra] >= 0)");
        });
        entita.HasKey(impostazioni => impostazioni.Id);
        entita.Property(impostazioni => impostazioni.Id).ValueGeneratedNever();
        entita.Property(impostazioni => impostazioni.Intestatario).HasMaxLength(ImpostazioniPagamentoOfflineSito.LunghezzaMassimaIntestatario);
        entita.Property(impostazioni => impostazioni.Iban).HasMaxLength(34);
        entita.Property(impostazioni => impostazioni.Bic).HasMaxLength(11);
        entita.Property(impostazioni => impostazioni.NomeBanca).HasMaxLength(ImpostazioniPagamentoOfflineSito.LunghezzaMassimaBanca);
        entita.Property(impostazioni => impostazioni.IstruzioniBonifico).HasMaxLength(ImpostazioniPagamentoOfflineSito.LunghezzaMassimaIstruzioni);
        entita.Property(impostazioni => impostazioni.IstruzioniContrassegno).HasMaxLength(ImpostazioniPagamentoOfflineSito.LunghezzaMassimaIstruzioni);
        entita.Property(impostazioni => impostazioni.CommissioneFissa).HasPrecision(18, 2);
        entita.Property(impostazioni => impostazioni.CommissionePercentuale).HasPrecision(5, 2);
        entita.Property(impostazioni => impostazioni.CommissioneGratisSopra).HasPrecision(18, 2);
        entita.Property(impostazioni => impostazioni.OrdineMinimo).HasPrecision(18, 2);
        entita.Property(impostazioni => impostazioni.OrdineMassimo).HasPrecision(18, 2);
        entita.Property(impostazioni => impostazioni.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(impostazioni => new { impostazioni.IdTenant, impostazioni.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Domain.Accessi.Utenza>().WithMany().HasForeignKey(impostazioni => impostazioni.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(impostazioni => impostazioni.IdSito).IsUnique();
    }
}

internal sealed class ConfigurazioneTentativoPagamento : IEntityTypeConfiguration<TentativoPagamento>
{
    public void Configure(EntityTypeBuilder<TentativoPagamento> entita)
    {
        entita.ToTable("TentativiPagamento", tabella => tabella.HasCheckConstraint("CK_TentativiPagamento_Importo", "[Importo] > 0"));
        entita.HasKey(tentativo => tentativo.Id);
        entita.Property(tentativo => tentativo.Id).ValueGeneratedNever();
        entita.Property(tentativo => tentativo.Provider).HasMaxLength(20).IsRequired();
        entita.Property(tentativo => tentativo.IdProvider).HasMaxLength(64).IsRequired();
        entita.Property(tentativo => tentativo.IdCattura).HasMaxLength(64);
        entita.Property(tentativo => tentativo.HashTokenNotifica).HasMaxLength(64);
        entita.Property(tentativo => tentativo.Importo).HasPrecision(18, 2);
        entita.Property(tentativo => tentativo.ImportoIncassato).HasPrecision(18, 2);
        entita.Property(tentativo => tentativo.Valuta).HasMaxLength(3).IsFixedLength().IsRequired();
        entita.Property(tentativo => tentativo.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();

        entita.HasOne<Ordine>()
            .WithMany()
            .HasForeignKey(tentativo => new { tentativo.IdTenant, tentativo.IdSito, tentativo.IdOrdine })
            .HasPrincipalKey(ordine => new { ordine.IdTenant, ordine.IdSito, ordine.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(tentativo => new { tentativo.IdSito, tentativo.Provider, tentativo.IdProvider }).IsUnique();
        entita.HasIndex(tentativo => tentativo.IdOrdine);
    }
}

internal sealed class ConfigurazioneEventoWebhook : IEntityTypeConfiguration<EventoWebhook>
{
    public void Configure(EntityTypeBuilder<EventoWebhook> entita)
    {
        entita.ToTable("EventiWebhook");
        entita.HasKey(evento => evento.Id);
        entita.Property(evento => evento.Id).ValueGeneratedNever();
        entita.Property(evento => evento.Provider).HasMaxLength(20).IsRequired();
        entita.Property(evento => evento.IdEvento).HasMaxLength(100).IsRequired();
        entita.Property(evento => evento.Tipo).HasMaxLength(100).IsRequired();
        entita.Property(evento => evento.Esito).HasMaxLength(20).IsRequired();

        entita.HasOne<Sito>()
            .WithMany()
            .HasForeignKey(evento => new { evento.IdTenant, evento.IdSito })
            .HasPrincipalKey(sito => new { sito.IdTenant, sito.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // La deduplica: lo stesso evento dello stesso provider non si registra due volte nello stesso sito.
        entita.HasIndex(evento => new { evento.IdSito, evento.Provider, evento.IdEvento }).IsUnique();
    }
}

internal sealed class ConfigurazioneRimborso : IEntityTypeConfiguration<Rimborso>
{
    public void Configure(EntityTypeBuilder<Rimborso> entita)
    {
        entita.ToTable("Rimborsi", tabella => tabella.HasCheckConstraint("CK_Rimborsi_Importo", "[Importo] > 0"));
        entita.HasKey(rimborso => rimborso.Id);
        entita.Property(rimborso => rimborso.Id).ValueGeneratedNever();
        entita.Property(rimborso => rimborso.Importo).HasPrecision(18, 2);
        entita.Property(rimborso => rimborso.Valuta).HasMaxLength(3).IsFixedLength().IsRequired();
        entita.Property(rimborso => rimborso.Motivo).HasMaxLength(Rimborso.LunghezzaMassimaMotivo);
        entita.Property(rimborso => rimborso.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(rimborso => rimborso.IdProvider).HasMaxLength(64);

        entita.HasOne<Ordine>()
            .WithMany()
            .HasForeignKey(rimborso => new { rimborso.IdTenant, rimborso.IdSito, rimborso.IdOrdine })
            .HasPrincipalKey(ordine => new { ordine.IdTenant, ordine.IdSito, ordine.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<TentativoPagamento>().WithMany().HasForeignKey(rimborso => rimborso.IdTentativo).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<DvCms.Domain.Accessi.Utenza>().WithMany().HasForeignKey(rimborso => rimborso.IdUtenzaRichiedente).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(rimborso => rimborso.IdOrdine);

        // Un rimborso del provider si registra una volta sola, anche se la notifica arriva due volte insieme.
        entita.HasIndex(rimborso => new { rimborso.IdSito, rimborso.IdProvider }).IsUnique().HasFilter("[IdProvider] IS NOT NULL");
    }
}

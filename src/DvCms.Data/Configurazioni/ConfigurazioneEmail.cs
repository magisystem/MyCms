using DvCms.Models.Accessi;
using DvCms.Models.Email;
using DvCms.Models.Segreti;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Data.Configurazioni;

internal sealed class ConfigurazioneImpostazioniEmailTenant : IEntityTypeConfiguration<ImpostazioniEmailTenant>
{
    public void Configure(EntityTypeBuilder<ImpostazioniEmailTenant> entita)
    {
        entita.ToTable("ImpostazioniEmailTenant");
        entita.HasKey(impostazioni => impostazioni.Id);
        entita.Property(impostazioni => impostazioni.Id).ValueGeneratedNever();
        entita.Property(impostazioni => impostazioni.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(impostazioni => impostazioni.ValoriJson).IsRequired();
        entita.Property(impostazioni => impostazioni.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Tenant>().WithMany().HasForeignKey(impostazioni => impostazioni.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(impostazioni => impostazioni.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(impostazioni => impostazioni.IdTenant).IsUnique();
    }
}

internal sealed class ConfigurazioneConnessioneEmailOAuth : IEntityTypeConfiguration<ConnessioneEmailOAuth>
{
    public void Configure(EntityTypeBuilder<ConnessioneEmailOAuth> entita)
    {
        entita.ToTable("ConnessioniEmailOAuth");
        entita.HasKey(connessione => connessione.Id);
        entita.Property(connessione => connessione.Id).ValueGeneratedNever();
        entita.Property(connessione => connessione.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(connessione => connessione.ImprontaApp).HasMaxLength(64).IsRequired();
        entita.Property(connessione => connessione.Casella).HasMaxLength(320).IsRequired();
        entita.Property(connessione => connessione.TokenAggiornamentoProtetto).IsRequired();
        entita.Property(connessione => connessione.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(connessione => connessione.UltimoErrore).HasMaxLength(200);
        entita.Property(connessione => connessione.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Tenant>().WithMany().HasForeignKey(connessione => connessione.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(connessione => connessione.CollegataDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(connessione => connessione.IdTenant).IsUnique();
    }
}

internal sealed class ConfigurazioneChiaveSegreta : IEntityTypeConfiguration<ChiaveSegreta>
{
    public void Configure(EntityTypeBuilder<ChiaveSegreta> entita)
    {
        entita.ToTable("ChiaviSegrete");
        entita.HasKey(chiave => chiave.Id);
        entita.Property(chiave => chiave.Id).ValueGeneratedNever();
        entita.Property(chiave => chiave.NomeAmichevole).HasMaxLength(200).IsRequired();
        entita.Property(chiave => chiave.Xml).IsRequired();
    }
}

internal sealed class ConfigurazioneModelloEmail : IEntityTypeConfiguration<ModelloEmail>
{
    public void Configure(EntityTypeBuilder<ModelloEmail> entita)
    {
        entita.ToTable("ModelliEmail");
        entita.HasKey(modello => modello.Id);
        entita.Property(modello => modello.Id).ValueGeneratedNever();
        entita.Property(modello => modello.Codice).HasMaxLength(60).IsRequired();
        entita.Property(modello => modello.Lingua).HasMaxLength(10).IsRequired();
        entita.Property(modello => modello.Oggetto).HasMaxLength(200).IsRequired();
        entita.Property(modello => modello.CorpoHtml).IsRequired();
        entita.Property(modello => modello.CorpoTesto).IsRequired();
        entita.Property(modello => modello.TokenConcorrenza).IsRowVersion();

        entita.HasOne<Tenant>().WithMany().HasForeignKey(modello => modello.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(modello => modello.AggiornatoDaIdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(modello => new { modello.IdTenant, modello.Codice, modello.Lingua }).IsUnique();
    }
}

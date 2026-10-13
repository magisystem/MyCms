using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Localizzazione;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneUtenza : IEntityTypeConfiguration<Utenza>
{
    public void Configure(EntityTypeBuilder<Utenza> entita)
    {
        entita.ToTable(
            "Utenze",
            tabella => tabella.HasCheckConstraint(
                "CK_Utenze_RuoloETenant",
                "([Ruolo] = 'PlatformAdmin' AND [IdTenant] IS NULL) OR ([Ruolo] <> 'PlatformAdmin' AND [IdTenant] IS NOT NULL)"));
        entita.HasKey(utenza => utenza.Id);
        entita.Property(utenza => utenza.Id).ValueGeneratedNever();
        entita.Property(utenza => utenza.Ruolo).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(utenza => utenza.Email).HasMaxLength(NormalizzatoreEmail.LunghezzaMassima).IsRequired();
        entita.Property(utenza => utenza.EmailNormalizzata).HasMaxLength(NormalizzatoreEmail.LunghezzaMassima).IsRequired();
        entita.Property(utenza => utenza.NomeVisualizzato).HasMaxLength(200).IsRequired();
        entita.Property(utenza => utenza.HashPassword).HasMaxLength(200).IsRequired();
        entita.Property(utenza => utenza.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(utenza => utenza.SegretoTotpProtetto).HasMaxLength(500);
        entita.Property(utenza => utenza.SegretoTotpInAttesaProtetto).HasMaxLength(500);
        entita.Property(utenza => utenza.TokenConcorrenza).IsRowVersion();

        // L'email è unica nel tenant e può ripetersi in tenant diversi. Fra i PlatformAdmin (IdTenant nullo) è unica
        // perché in SQL Server i NULL sono considerati uguali in un indice univoco.
        // HasFilter(null) toglie il filtro "IS NOT NULL" che EF Core aggiunge di default agli indici univoci con colonne nullable.
        entita.HasIndex(utenza => new { utenza.IdTenant, utenza.EmailNormalizzata }).IsUnique().HasFilter(null);

        entita.HasOne<Tenant>().WithMany().HasForeignKey(utenza => utenza.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Lingua>().WithMany().HasForeignKey(utenza => utenza.IdLingua).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurazioneRegistroAudit : IEntityTypeConfiguration<RegistroAudit>
{
    public void Configure(EntityTypeBuilder<RegistroAudit> entita)
    {
        entita.ToTable("RegistroAudit");
        entita.HasKey(voce => voce.Id);
        entita.Property(voce => voce.Id).ValueGeneratedNever();
        entita.Property(voce => voce.Azione).HasMaxLength(100).IsRequired();
        entita.Property(voce => voce.Risorsa).HasMaxLength(200).IsRequired();
        entita.Property(voce => voce.Esito).HasMaxLength(20).IsRequired();
        entita.Property(voce => voce.Dettagli).HasMaxLength(RegistroAudit.LunghezzaMassimaDettagli);

        entita.HasOne<Tenant>().WithMany().HasForeignKey(voce => voce.IdTenant).OnDelete(DeleteBehavior.Restrict);
        entita.HasOne<Utenza>().WithMany().HasForeignKey(voce => voce.IdUtenzaAttore).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(voce => new { voce.IdTenant, voce.CreatoIlUtc });
    }
}

internal sealed class ConfigurazioneCodiceRecuperoUtenza : IEntityTypeConfiguration<CodiceRecuperoUtenza>
{
    public void Configure(EntityTypeBuilder<CodiceRecuperoUtenza> entita)
    {
        entita.ToTable("CodiciRecuperoUtenza");
        entita.HasKey(codice => codice.Id);
        entita.Property(codice => codice.Id).ValueGeneratedNever();
        entita.Property(codice => codice.HashCodice).HasColumnType("binary(32)").IsRequired();

        entita.HasOne<Utenza>().WithMany().HasForeignKey(codice => codice.IdUtenza).OnDelete(DeleteBehavior.Restrict);
        entita.HasIndex(codice => new { codice.IdUtenza, codice.HashCodice }).IsUnique();
    }
}

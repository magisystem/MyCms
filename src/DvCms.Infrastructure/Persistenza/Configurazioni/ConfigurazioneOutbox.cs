using DvCms.Domain.Outbox;
using DvCms.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneMessaggioOutbox : IEntityTypeConfiguration<MessaggioOutbox>
{
    public void Configure(EntityTypeBuilder<MessaggioOutbox> entita)
    {
        entita.ToTable("MessaggiOutbox");
        entita.HasKey(messaggio => messaggio.Id);
        entita.Property(messaggio => messaggio.Id).ValueGeneratedNever();
        entita.Property(messaggio => messaggio.Tipo).HasMaxLength(MessaggioOutbox.LunghezzaMassimaTipo).IsRequired();
        entita.Property(messaggio => messaggio.Payload).IsRequired();
        entita.Property(messaggio => messaggio.ChiaveIdempotenza).HasMaxLength(MessaggioOutbox.LunghezzaMassimaChiave);
        entita.Property(messaggio => messaggio.Stato).HasConversion<string>().HasMaxLength(20).IsRequired();
        entita.Property(messaggio => messaggio.UltimoErrore).HasMaxLength(MessaggioOutbox.LunghezzaMassimaErrore);

        entita.HasOne<Tenant>().WithMany().HasForeignKey(messaggio => messaggio.IdTenant).OnDelete(DeleteBehavior.Restrict);

        // Per il worker: i messaggi pendenti in ordine di prossimo tentativo.
        entita.HasIndex(messaggio => new { messaggio.Stato, messaggio.ProssimoTentativoIlUtc });

        // Lo stesso evento (stessa chiave) non si accoda due volte per lo stesso tenant.
        entita.HasIndex(messaggio => new { messaggio.IdTenant, messaggio.ChiaveIdempotenza })
            .IsUnique()
            .HasFilter("[ChiaveIdempotenza] IS NOT NULL")
            .HasDatabaseName("IX_MessaggiOutbox_Idempotenza");
    }
}

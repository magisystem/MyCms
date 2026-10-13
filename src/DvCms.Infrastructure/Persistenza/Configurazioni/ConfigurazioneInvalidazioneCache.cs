using DvCms.Models.Cms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DvCms.Infrastructure.Persistenza.Configurazioni;

internal sealed class ConfigurazioneInvalidazioneCache : IEntityTypeConfiguration<InvalidazioneCache>
{
    public void Configure(EntityTypeBuilder<InvalidazioneCache> entita)
    {
        entita.ToTable("InvalidazioniCache");
        entita.HasKey(riga => riga.Id);
        entita.Property(riga => riga.Id).ValueGeneratedOnAdd();

        // Nessuna chiave esterna: il registro è di sola lettura per le istanze e si ripulisce a tempo; non deve mai bloccare una cancellazione.
        entita.HasIndex(riga => riga.CreatoIlUtc);
    }
}

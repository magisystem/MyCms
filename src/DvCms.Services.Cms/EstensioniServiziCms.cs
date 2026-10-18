using DvCms.Contracts.Accessi;
using DvCms.Contracts.Accessi.DueFattori;
using DvCms.Contracts.Builder;
using DvCms.Contracts.Cms;
using DvCms.Contracts.Email;
using DvCms.Contracts.Localizzazione;
using DvCms.Contracts.Manutenzione;
using DvCms.Contracts.Media;
using DvCms.Contracts.Moduli;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Outbox;
using DvCms.Contracts.Persistenza;
using DvCms.Contracts.Piattaforma;
using DvCms.Contracts.Temi;
using DvCms.Contracts.Tenancy;
using DvCms.Core.Accessi;
using DvCms.Core.Builder;
using DvCms.Core.Cache;
using DvCms.Core.Cms;
using DvCms.Core.Configurazione;
using DvCms.Core.Localizzazione;
using DvCms.Core.Media;
using DvCms.Core.Segreti;
using DvCms.Core.Temi;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.Data.Cache;
using DvCms.Data.Outbox;
using DvCms.Data.Popolamento;
using DvCms.Data.Segreti;
using DvCms.Data.Tenancy;
using DvCms.DTO.Accessi;
using DvCms.DTO.Accessi.DueFattori;
using DvCms.DTO.Cache;
using DvCms.DTO.Manutenzione;
using DvCms.DTO.Media;
using DvCms.DTO.Moduli;
using DvCms.DTO.Negozio;
using DvCms.DTO.Outbox;
using DvCms.Services.Cms.Media;
using DvCms.Services.Cms.Moduli;
using DvCms.Services.Cms.Temi;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DvCms.Services.Cms;

public static class EstensioniServiziCms
{
    /// <summary>Registra pagine, builder, pagine pubbliche con cache, sitemap, temi e media. Presuppone <c>AggiungiData</c> (ADR-0024).</summary>
    public static IServiceCollection AggiungiServiziCms(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        servizi.TryAddScoped<ServizioSitemap>();
        servizi.TryAddScoped<IServizioSitemap>(f => new ServizioSitemapConCache(f.GetRequiredService<ServizioSitemap>(), f.GetRequiredService<ICachePubblica>(), f.GetRequiredService<IContestoTenant>()));
        servizi.TryAddSingleton(new RegistroTemi(RegistroTemi.Predefiniti));
        servizi.TryAddScoped<IServizioTemi, ServizioTemi>();
        servizi.TryAddScoped<RisolutoreTema>();
        servizi.TryAddScoped<IRisolutoreTema>(f => new RisolutoreTemaConCache(f.GetRequiredService<RisolutoreTema>(), f.GetRequiredService<ICachePubblica>(), f.GetRequiredService<IContestoTenant>()));
        servizi.TryAddSingleton(new RegistroModelliPagina(RegistroModelliPagina.Predefiniti));
        servizi.TryAddSingleton(new RegistroComponenti(RegistroComponenti.Predefiniti));
        servizi.TryAddSingleton(new RegistroTemplateBuilder(RegistroTemplateBuilder.Predefiniti));
        servizi.TryAddScoped<IServizioBuilder, ServizioBuilder>();
        servizi.TryAddScoped<IServizioPagine, ServizioPagine>();
        servizi.TryAddScoped<ServizioPaginePubbliche>();
        servizi.TryAddScoped<IServizioPaginePubbliche>(f => new ServizioPaginePubblicheConCache(f.GetRequiredService<ServizioPaginePubbliche>(), f.GetRequiredService<ICachePubblica>(), f.GetRequiredService<IContestoTenant>()));
        servizi.AddScoped<IGestoreOutbox, GestorePaginaPubblicata>();
        servizi.AddScoped<IGestoreOutbox, GestorePaginaRitirata>();
        servizi.AddScoped<IGestoreOutbox, GestorePaginaRinominata>();
        servizi.TryAddScoped<IServizioModuli, ServizioModuli>();
        servizi.TryAddSingleton(new OpzioniModuli(configurazione.LeggiIntero("Moduli:InviiMassimiAlGiorno", OpzioniModuli.InviiMassimiPredefiniti, 1, 100_000)));
        servizi.TryAddScoped<NotificheModulo>();
        servizi.TryAddScoped<IServizioModuloPubblico, ServizioModuloPubblico>();
        servizi.TryAddScoped<IServizioRichiesteModulo, ServizioRichiesteModulo>();
        servizi.AddScoped<IAttivitaManutenzione, ManutenzioneModuli>();
        AggiungiMedia(servizi, configurazione);

        return servizi;
    }

    private static void AggiungiMedia(IServiceCollection servizi, IConfiguration configurazione)
    {
        var p = OpzioniMedia.Predefinite;
        servizi.TryAddSingleton(new OpzioniMedia(
            configurazione.LeggiIntero("Media:DimensioneMassimaKb", (int)(p.DimensioneMassima / 1024), 1, 102_400) * 1024L,
            configurazione.LeggiIntero("Media:LatoMassimoImmagine", p.LatoMassimoImmagine, 1, 65_535),
            configurazione.LeggiIntero("Media:PixelMassimiMilioni", (int)(p.PixelMassimi / 1_000_000), 1, 1000) * 1_000_000L));

        // Archivio degli oggetti: provider scelto da configurazione; uno sconosciuto ferma l'avvio invece di ripiegare su altro.
        var provider = configurazione["Archivio:Provider"] ?? "FileSystem";
        switch (provider)
        {
            case "FileSystem":
                var radice = configurazione["Archivio:Percorso"];
                servizi.TryAddSingleton<IArchivioOggetti>(_ => new ArchivioFileSystem(
                    string.IsNullOrWhiteSpace(radice) ? Path.Combine(AppContext.BaseDirectory, "archivio") : radice));
                break;
            default:
                throw new InvalidOperationException($"Provider dell'archivio '{provider}' non riconosciuto (valori ammessi: FileSystem).");
        }

        servizi.TryAddScoped<IServizioMedia, ServizioMedia>();
        servizi.TryAddScoped<VerificatoreArchivio>();
    }
}

using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Segreti;
using DvCms.Models.Accessi;
using DvCms.Models.Cms;
using DvCms.Models.Email;
using DvCms.Models.Localizzazione;
using DvCms.Models.Media;
using DvCms.Models.Negozio;
using DvCms.Models.Outbox;
using DvCms.Models.Temi;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Persistenza;

/// <summary>
/// Contesto EF Core dell'applicazione (SQL Server). Le entità e le mappature delle tabelle si aggiungono qui,
/// con nomi di tabelle e colonne in italiano (ADR-0017) e mappatura esplicita.
/// </summary>
public class ContestoDatabase : DbContext
{
    public const string NomeTabellaStoricoMigrazioni = "StoricoMigrazioni";

    private readonly IContestoTenant _contestoTenant;

    public ContestoDatabase(DbContextOptions<ContestoDatabase> opzioni, IContestoTenant contestoTenant)
        : base(opzioni)
    {
        _contestoTenant = contestoTenant;
    }

    protected ContestoDatabase(DbContextOptions opzioni, IContestoTenant contestoTenant)
        : base(opzioni)
    {
        _contestoTenant = contestoTenant;
    }

    /// <summary>
    /// Tenant della richiesta, usato dai filtri globali: i dati con <c>IdTenant</c> sono visibili solo per questo tenant;
    /// senza tenant risolto non è visibile nessuna riga. Le letture di piattaforma (tutti i tenant) usano
    /// esplicitamente <c>IgnoreQueryFilters()</c> in casi d'uso privilegiati e verificati da test.
    /// </summary>
    public Guid? IdTenantCorrente => _contestoTenant.IdTenantOpzionale;

    public DbSet<Lingua> Lingue => Set<Lingua>();

    public DbSet<TestoStatico> TestiStatici => Set<TestoStatico>();

    public DbSet<Tenant> Tenant => Set<Tenant>();

    public DbSet<Sito> Siti => Set<Sito>();

    public DbSet<DominioSito> DominiSito => Set<DominioSito>();

    public DbSet<LinguaSito> LingueSito => Set<LinguaSito>();

    public DbSet<ProfiloGuest> ProfiliGuest => Set<ProfiloGuest>();

    public DbSet<Utenza> Utenze => Set<Utenza>();

    public DbSet<Pagina> Pagine => Set<Pagina>();

    public DbSet<RevisionePagina> RevisioniPagina => Set<RevisionePagina>();

    public DbSet<RedirectPagina> RedirectPagine => Set<RedirectPagina>();

    public DbSet<ImpostazioniEmailTenant> ImpostazioniEmailTenant => Set<ImpostazioniEmailTenant>();

    public DbSet<TermineRicercaProdotto> TerminiRicercaProdotto => Set<TermineRicercaProdotto>();

    public DbSet<ConnessioneEmailOAuth> ConnessioniEmailOAuth => Set<ConnessioneEmailOAuth>();

    public DbSet<ChiaveSegreta> ChiaviSegrete => Set<ChiaveSegreta>();

    public DbSet<MessaggioOutbox> MessaggiOutbox => Set<MessaggioOutbox>();

    public DbSet<RichiestaRecuperoPassword> RichiesteRecuperoPassword => Set<RichiestaRecuperoPassword>();

    public DbSet<InvalidazioneCache> InvalidazioniCache => Set<InvalidazioneCache>();

    public DbSet<RisorsaMedia> RisorseMedia => Set<RisorsaMedia>();

    public DbSet<TraduzioneRisorsaMedia> TraduzioniRisorseMedia => Set<TraduzioneRisorsaMedia>();

    public DbSet<ImpostazioniTemaSito> ImpostazioniTemaSito => Set<ImpostazioniTemaSito>();

    public DbSet<InvitoAccount> InvitiAccount => Set<InvitoAccount>();

    public DbSet<CodiceRecuperoUtenza> CodiciRecuperoUtenza => Set<CodiceRecuperoUtenza>();

    public DbSet<ConfermaEmailUtenza> ConfermeEmailUtenza => Set<ConfermaEmailUtenza>();

    public DbSet<RegistroAudit> RegistroAudit => Set<RegistroAudit>();

    public DbSet<Prodotto> Prodotti => Set<Prodotto>();

    public DbSet<VarianteProdotto> VariantiProdotto => Set<VarianteProdotto>();

    public DbSet<TraduzioneProdotto> TraduzioniProdotto => Set<TraduzioneProdotto>();

    public DbSet<ImmagineProdotto> ImmaginiProdotto => Set<ImmagineProdotto>();

    public DbSet<Categoria> Categorie => Set<Categoria>();

    public DbSet<TraduzioneCategoria> TraduzioniCategoria => Set<TraduzioneCategoria>();

    public DbSet<ProdottoCategoria> ProdottiCategoria => Set<ProdottoCategoria>();

    public DbSet<TraduzioneVarianteProdotto> TraduzioniVarianteProdotto => Set<TraduzioneVarianteProdotto>();

    public DbSet<Carrello> Carrelli => Set<Carrello>();

    public DbSet<RigaCarrello> RigheCarrello => Set<RigaCarrello>();

    public DbSet<Ordine> Ordini => Set<Ordine>();

    public DbSet<RigaOrdine> RigheOrdine => Set<RigaOrdine>();

    public DbSet<TentativoPagamento> TentativiPagamento => Set<TentativoPagamento>();

    public DbSet<Rimborso> Rimborsi => Set<Rimborso>();

    public DbSet<EventoWebhook> EventiWebhook => Set<EventoWebhook>();

    public DbSet<ImpostazioniPagamentoSito> ImpostazioniPagamentoSiti => Set<ImpostazioniPagamentoSito>();

    public DbSet<ImpostazioniNexiSito> ImpostazioniNexiSiti => Set<ImpostazioniNexiSito>();

    public DbSet<ImpostazioniPagamentoOfflineSito> ImpostazioniPagamentoOfflineSiti => Set<ImpostazioniPagamentoOfflineSito>();

    public DbSet<GiacenzaVariante> GiacenzeVarianti => Set<GiacenzaVariante>();

    public DbSet<PrenotazioneGiacenza> PrenotazioniGiacenza => Set<PrenotazioneGiacenza>();

    public DbSet<MovimentoGiacenza> MovimentiGiacenza => Set<MovimentoGiacenza>();

    public DbSet<MetodoSpedizione> MetodiSpedizione => Set<MetodoSpedizione>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContestoDatabase).Assembly);

        // Isolamento tenant (difesa a più livelli, docs/architecture/multi-tenancy.md): filtro globale sui dati con IdTenant.
        modelBuilder.Entity<Sito>().HasQueryFilter(sito => sito.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<DominioSito>().HasQueryFilter(dominio => dominio.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<LinguaSito>().HasQueryFilter(lingua => lingua.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ProfiloGuest>().HasQueryFilter(profilo => profilo.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Pagina>().HasQueryFilter(pagina => pagina.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RevisionePagina>().HasQueryFilter(revisione => revisione.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RedirectPagina>().HasQueryFilter(redirect => redirect.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ImpostazioniEmailTenant>().HasQueryFilter(impostazioni => impostazioni.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<TermineRicercaProdotto>().HasQueryFilter(termine => termine.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ConnessioneEmailOAuth>().HasQueryFilter(connessione => connessione.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RichiestaRecuperoPassword>().HasQueryFilter(richiesta => richiesta.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RisorsaMedia>().HasQueryFilter(risorsa => risorsa.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<TraduzioneRisorsaMedia>().HasQueryFilter(traduzione => traduzione.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ImpostazioniTemaSito>().HasQueryFilter(impostazioni => impostazioni.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<InvitoAccount>().HasQueryFilter(invito => invito.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ConfermaEmailUtenza>().HasQueryFilter(conferma => conferma.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<MessaggioOutbox>().HasQueryFilter(messaggio => messaggio.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Utenza>().HasQueryFilter(utenza => utenza.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RegistroAudit>().HasQueryFilter(voce => voce.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Prodotto>().HasQueryFilter(prodotto => prodotto.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<VarianteProdotto>().HasQueryFilter(variante => variante.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<TraduzioneProdotto>().HasQueryFilter(traduzione => traduzione.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ImmagineProdotto>().HasQueryFilter(immagine => immagine.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Categoria>().HasQueryFilter(categoria => categoria.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<TraduzioneCategoria>().HasQueryFilter(traduzione => traduzione.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ProdottoCategoria>().HasQueryFilter(appartenenza => appartenenza.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<TraduzioneVarianteProdotto>().HasQueryFilter(traduzione => traduzione.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Carrello>().HasQueryFilter(carrello => carrello.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RigaCarrello>().HasQueryFilter(riga => riga.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Ordine>().HasQueryFilter(ordine => ordine.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RigaOrdine>().HasQueryFilter(riga => riga.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<TentativoPagamento>().HasQueryFilter(tentativo => tentativo.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Rimborso>().HasQueryFilter(rimborso => rimborso.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<EventoWebhook>().HasQueryFilter(evento => evento.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ImpostazioniPagamentoSito>().HasQueryFilter(impostazioni => impostazioni.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ImpostazioniNexiSito>().HasQueryFilter(impostazioni => impostazioni.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ImpostazioniPagamentoOfflineSito>().HasQueryFilter(impostazioni => impostazioni.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<GiacenzaVariante>().HasQueryFilter(giacenza => giacenza.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<PrenotazioneGiacenza>().HasQueryFilter(prenotazione => prenotazione.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<MovimentoGiacenza>().HasQueryFilter(movimento => movimento.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<MetodoSpedizione>().HasQueryFilter(metodo => metodo.IdTenant == IdTenantCorrente);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Convenzioni del modello (docs/architecture/database.md): istanti UTC in datetime2 e importi con precisione esplicita.
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("datetime2")
            .HaveConversion<ConvertitoreDataOraUtc>();
        configurationBuilder.Properties<decimal>()
            .HavePrecision(18, 4);
    }
}

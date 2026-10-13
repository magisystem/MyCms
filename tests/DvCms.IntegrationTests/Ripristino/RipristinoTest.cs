using System.Diagnostics;
using System.Security.Cryptography;
using DvCms.Application.Accessi;
using DvCms.Application.Cms;
using DvCms.Application.Email;
using DvCms.Application.Media;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Media;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Email;
using DvCms.Models.Outbox;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Ripristino;

/// <summary>
/// Prova di backup e ripristino (G-004) su SQL Server reale: backup completo con checksum, ripristino con un altro nome, copia
/// dell'archivio dei file e verifica che tutto sia identico e utilizzabile. Le stesse istruzioni sono nel runbook
/// (<c>docs/operations/backup-e-ripristino.md</c>): se il runbook cambia, cambia anche questo test.
/// </summary>
public sealed class RipristinoTest : IAsyncDisposable
{
    private static readonly string _chiave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string _altraChiave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly string _archivioOriginale = Path.Combine(Path.GetTempPath(), "dvcms-ripristino-origine-" + Guid.NewGuid().ToString("N"));
    private readonly string _archivioRipristinato = Path.Combine(Path.GetTempPath(), "dvcms-ripristino-copia-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _databaseDaEliminare = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlDatabaseRipristinatoEIdenticoELApplicazioneLoUsaConLeStesseChiavi()
    {
        await using var origine = await DatabaseDiProva.CreaAsync(Ct);
        await using var fornitoreOrigine = await CreaFornitoreAsync(origine.Connessione, _chiave, _archivioOriginale);
        var dati = await PopolaAsync(fornitoreOrigine);
        var impronteOrigine = await ImpronteTabelleAsync(origine.Connessione);
        Assert.True(impronteOrigine.Count > 20);
        Assert.Contains(impronteOrigine.Values, i => i.Righe > 0);

        // Backup completo con checksum e controllo, ripristino con un altro nome e copia dell'archivio dei file.
        var nomeRipristinato = $"DvCmsRipristino_{Guid.NewGuid():N}";
        _databaseDaEliminare.Add(nomeRipristinato);
        var cronometro = Stopwatch.StartNew();
        var connessioneServer = ConfigurazioneProva.RichiediConnessioneServer();
        var file = await EseguiBackupERipristinoAsync(connessioneServer, origine.Nome, nomeRipristinato);
        var durata = cronometro.Elapsed;
        CopiaDirectory(_archivioOriginale, _archivioRipristinato);

        var connessioneRipristinata = new SqlConnectionStringBuilder(connessioneServer) { InitialCatalog = nomeRipristinato }.ConnectionString;
        var impronteRipristinate = await ImpronteTabelleAsync(connessioneRipristinata);
        Assert.Equal(impronteOrigine, impronteRipristinate);
        Assert.True(durata < TimeSpan.FromMinutes(2), $"Backup e ripristino hanno richiesto {durata} ({file})");

        // L'applicazione parte sul database ripristinato con la stessa chiave dei segreti: nessuna migrazione da applicare.
        await using var ripristinato = await CreaFornitoreAsync(connessioneRipristinata, _chiave, _archivioRipristinato, migra: false);
        await using (var ambito = ripristinato.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            Assert.Empty(await contesto.Database.GetPendingMigrationsAsync(Ct));

            // Account: la password funziona (hash BCrypt ripristinato così com'è).
            var utenza = await contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == dati.IdAdmin, Ct);
            Assert.True(ambito.ServiceProvider.GetRequiredService<IHashPassword>().Verifica(DatiProva.PasswordProva, utenza.HashPassword));

            // Segreti del tenant: decifrabili con la chiave giusta.
            var impostazioni = await contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().SingleAsync(Ct);
            Assert.Contains("SEGRETO-SMTP", ambito.ServiceProvider.GetRequiredService<IProtettoreSegreti>().Rivela(impostazioni.SegretiProtetti!), StringComparison.Ordinal);

            // Coda: il messaggio pendente è ancora lì e verrà elaborato.
            Assert.Equal(1, await contesto.MessaggiOutbox.IgnoreQueryFilters().CountAsync(m => m.Tipo == "Prova" && m.Stato == StatoMessaggioOutbox.Pendente, Ct));
            Assert.Equal(2, await contesto.MessaggiOutbox.IgnoreQueryFilters().CountAsync(m => m.Stato == StatoMessaggioOutbox.Pendente, Ct));

            // Archivio: ogni risorsa ha il suo file con l'impronta registrata.
            var esito = await ambito.ServiceProvider.GetRequiredService<VerificatoreArchivio>().VerificaAsync(Ct);
            Assert.True(esito.Coerente);
            Assert.Equal(1, esito.RisorseControllate);
            Assert.Empty(esito.OggettiOrfani);
        }

        // Sito pubblico: la pagina e il file si servono dal ripristino.
        await using (var ambito = ripristinato.CreateAsyncScope())
        {
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(dati.Tenant.IdTenant, dati.Tenant.IdSito, dati.Tenant.IdDominio, dati.Tenant.Host, false));
            var risoluzione = await ambito.ServiceProvider.GetRequiredService<IServizioPaginePubbliche>().RisolviAsync("chi-siamo", null, Ct);
            Assert.Equal("Chi siamo", risoluzione.Pagina?.Titolo);
            await using var flusso = (await ambito.ServiceProvider.GetRequiredService<IServizioMedia>().ApriPubblicaAsync(dati.IdMedia, Ct))!.Flusso;
            Assert.Equal(dati.ImmagineHash, await SHA256.HashDataAsync(flusso, Ct));
        }
    }

    [Fact]
    public async Task SenzaLaChiaveDeiSegretiIlDatabaseRipristinatoNonLiLegge()
    {
        await using var origine = await DatabaseDiProva.CreaAsync(Ct);
        await using (var fornitore = await CreaFornitoreAsync(origine.Connessione, _chiave, _archivioOriginale))
        {
            await PopolaAsync(fornitore);
        }

        // Stesso database, chiave sbagliata (o persa): i segreti non si decifrano e l'errore non mostra valori.
        await using var senzaChiave = await CreaFornitoreAsync(origine.Connessione, _altraChiave, _archivioOriginale, migra: false);
        await using var ambito = senzaChiave.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var impostazioni = await contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().SingleAsync(Ct);

        var errore = Assert.ThrowsAny<Exception>(() => ambito.ServiceProvider.GetRequiredService<IProtettoreSegreti>().Rivela(impostazioni.SegretiProtetti!));

        Assert.DoesNotContain("SEGRETO-SMTP", errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_chiave, errore.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaVerificaDellArchivioTrovaMancantiContenutiDiversiEOrfani()
    {
        await using var origine = await DatabaseDiProva.CreaAsync(Ct);
        await using var fornitore = await CreaFornitoreAsync(origine.Connessione, _chiave, _archivioOriginale);
        await PopolaAsync(fornitore);
        var chiave = await fornitore.CreateAsyncScope().ServiceProvider.GetRequiredService<ContestoDatabase>().RisorseMedia.IgnoreQueryFilters().Select(r => r.ChiaveOggetto).SingleAsync(Ct);
        var percorso = Path.Combine(_archivioOriginale, chiave.Replace('/', Path.DirectorySeparatorChar));
        async Task<EsitoVerificaArchivio> VerificaAsync()
        {
            await using var ambito = fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<VerificatoreArchivio>().VerificaAsync(Ct);
        }

        Assert.True((await VerificaAsync()).Coerente);

        // Contenuto alterato: stessa dimensione, byte diversi.
        var originale = await File.ReadAllBytesAsync(percorso, Ct);
        var alterato = (byte[])originale.Clone();
        alterato[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(percorso, alterato, Ct);
        var diversa = await VerificaAsync();
        Assert.False(diversa.Coerente);
        Assert.Equal([chiave], diversa.ImprontaDiversa);

        // File mancante, più un oggetto senza riga (orfano, che non rende l'archivio incoerente).
        File.Delete(percorso);
        var orfano = Path.Combine(_archivioOriginale, "0123456789abcdef", "0123456789abcdef", "0123456789abcdef");
        Directory.CreateDirectory(Path.GetDirectoryName(orfano)!);
        await File.WriteAllBytesAsync(orfano, [1, 2, 3], Ct);
        var mancante = await VerificaAsync();
        Assert.False(mancante.Coerente);
        Assert.Equal([chiave], mancante.OggettiMancanti);
        Assert.Equal(["0123456789abcdef/0123456789abcdef/0123456789abcdef"], mancante.OggettiOrfani);

        File.Delete(orfano);
        await File.WriteAllBytesAsync(percorso, originale, Ct);
        Assert.True((await VerificaAsync()).Coerente);
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        var connessioneServer = ConfigurazioneProva.RichiediConnessioneServer();
        foreach (var nome in _databaseDaEliminare)
        {
            await DatabaseDiProva.EseguiAsync(
                connessioneServer,
                $"IF DB_ID(N'{nome}') IS NOT NULL BEGIN ALTER DATABASE [{nome}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nome}]; END",
                CancellationToken.None);
        }

        foreach (var cartella in new[] { _archivioOriginale, _archivioRipristinato })
        {
            if (Directory.Exists(cartella))
            {
                Directory.Delete(cartella, recursive: true);
            }
        }
    }

    private static async Task<ServiceProvider> CreaFornitoreAsync(string connessione, string chiave, string archivio, bool migra = true)
    {
        var impostazioni = new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = chiave, ["Archivio:Percorso"] = archivio };
        return migra
            ? await ServiziProva.CreaConMigrazioniAsync(connessione, impostazioni, annullamento: Ct)
            : await ServiziProva.CreaSenzaMigrazioniAsync(connessione, impostazioni);
    }

    private static async Task<DatiPopolati> PopolaAsync(ServiceProvider fornitore)
    {
        var tenant = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
        var altro = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: Ct);
        var admin = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, tenant.IdTenant, "anna@esempio.it", annullamento: Ct);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, altro.IdTenant, "bruno@esempio.it", annullamento: Ct);

        await using var ambito = fornitore.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));

        var pagine = ambito.ServiceProvider.GetRequiredService<IServizioPagine>();
        var creata = await pagine.CreaAsync(admin, new DatiNuovaPagina("chi-siamo", "Chi siamo", "Testo della pagina.", "it", false), Ct);
        Assert.True(creata.Riuscito, creata.Codice.ToString());
        var dettaglio = (await pagine.LeggiAsync(admin, creata.Id!.Value, Ct))!;
        Assert.True((await pagine.PubblicaAsync(admin, creata.Id.Value, dettaglio.TokenConcorrenza, Ct)).Riuscito);

        var immagine = ImmaginiProva.Png(64, 48);
        var media = await ambito.ServiceProvider.GetRequiredService<IServizioMedia>().CaricaAsync(admin, new FileDaCaricare(new MemoryStream(immagine), "logo.png", "image/png"), Ct);
        Assert.True(media.Riuscito, media.Codice.ToString());

        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var protettore = ambito.ServiceProvider.GetRequiredService<IProtettoreSegreti>();
        contesto.ImpostazioniEmailTenant.Add(new ImpostazioniEmailTenant
        {
            Id = Guid.NewGuid(),
            IdTenant = tenant.IdTenant,
            Provider = ProviderEmail.Smtp,
            ValoriJson = "{\"Host\":\"smtp.esempio.it\",\"Porta\":\"587\",\"ModalitaTls\":\"StartTls\",\"IndirizzoMittente\":\"noreply@esempio.it\"}",
            SegretiProtetti = protettore.Proteggi("{\"Password\":\"SEGRETO-SMTP\"}"),
            AggiornatoIlUtc = DateTime.UtcNow,
        });
        contesto.MessaggiOutbox.Add(new MessaggioOutbox
        {
            Id = Guid.NewGuid(),
            IdTenant = tenant.IdTenant,
            Tipo = "Prova",
            Payload = "{}",
            Stato = StatoMessaggioOutbox.Pendente,
            CreatoIlUtc = DateTime.UtcNow,
            ProssimoTentativoIlUtc = DateTime.UtcNow,
        });
        await contesto.SaveChangesAsync(Ct);
        return new DatiPopolati(tenant, admin, media.Id!.Value, SHA256.HashData(immagine));
    }

    /// <summary>Per ogni tabella: numero di righe e somma di controllo del contenuto (rowversion compresi).</summary>
    private static async Task<Dictionary<string, (long Righe, int? Controllo)>> ImpronteTabelleAsync(string connessione)
    {
        var risultato = new Dictionary<string, (long, int?)>(StringComparer.Ordinal);
        await using var sql = new SqlConnection(connessione);
        await sql.OpenAsync(Ct);
        var tabelle = new List<string>();
        await using (var comando = new SqlCommand("SELECT name FROM sys.tables ORDER BY name", sql))
        await using (var lettore = await comando.ExecuteReaderAsync(Ct))
        {
            while (await lettore.ReadAsync(Ct))
            {
                tabelle.Add(lettore.GetString(0));
            }
        }

        foreach (var tabella in tabelle)
        {
#pragma warning disable CA2100 // Il nome viene da sys.tables, non da input esterno.
            await using var comando = new SqlCommand($"SELECT COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM [{tabella}]", sql);
#pragma warning restore CA2100
            await using var lettore = await comando.ExecuteReaderAsync(Ct);
            await lettore.ReadAsync(Ct);
            risultato[tabella] = (lettore.GetInt64(0), lettore.IsDBNull(1) ? null : lettore.GetInt32(1));
        }

        return risultato;
    }

    /// <summary>Le istruzioni del runbook: backup completo con checksum, controllo, ripristino con altro nome e file spostati.</summary>
    private static async Task<string> EseguiBackupERipristinoAsync(string connessioneServer, string origine, string destinazione)
    {
        var file = $"/var/opt/mssql/data/{destinazione}.bak";
        await DatabaseDiProva.EseguiAsync(connessioneServer, $"BACKUP DATABASE [{origine}] TO DISK = N'{file}' WITH INIT, CHECKSUM, COMPRESSION", Ct);
        await DatabaseDiProva.EseguiAsync(connessioneServer, $"RESTORE VERIFYONLY FROM DISK = N'{file}' WITH CHECKSUM", Ct);

        var spostamenti = new List<string>();
        await using (var sql = new SqlConnection(connessioneServer))
        {
            await sql.OpenAsync(Ct);
#pragma warning disable CA2100 // Il percorso è costruito dal test con un nome generato localmente.
            await using var comando = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = N'{file}'", sql);
#pragma warning restore CA2100
            await using var lettore = await comando.ExecuteReaderAsync(Ct);
            while (await lettore.ReadAsync(Ct))
            {
                var logico = lettore.GetString(lettore.GetOrdinal("LogicalName"));
                var estensione = lettore.GetString(lettore.GetOrdinal("Type")) == "L" ? "ldf" : "mdf";
                spostamenti.Add($"MOVE N'{logico}' TO N'/var/opt/mssql/data/{destinazione}_{logico}.{estensione}'");
            }
        }

        await DatabaseDiProva.EseguiAsync(connessioneServer, $"RESTORE DATABASE [{destinazione}] FROM DISK = N'{file}' WITH CHECKSUM, {string.Join(", ", spostamenti)}", Ct);
        return file;
    }

    private static void CopiaDirectory(string origine, string destinazione)
    {
        foreach (var file in Directory.EnumerateFiles(origine, "*", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(origine, file);
            var percorso = Path.Combine(destinazione, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
            File.Copy(file, percorso);
        }
    }

    private sealed record DatiPopolati(DatiProva.TenantCreato Tenant, Guid IdAdmin, Guid IdMedia, byte[] ImmagineHash);
}

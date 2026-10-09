using Microsoft.Data.SqlClient;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>
/// Database SQL Server temporaneo e vuoto, creato per un solo test ed eliminato al termine.
/// </summary>
public sealed class DatabaseDiProva : IAsyncDisposable
{
    private readonly string _connessioneServer;

    private DatabaseDiProva(string connessioneServer, string nome, string connessione)
    {
        _connessioneServer = connessioneServer;
        Nome = nome;
        Connessione = connessione;
    }

    public string Nome { get; }

    /// <summary>Stringa di connessione al database temporaneo.</summary>
    public string Connessione { get; }

    public static async Task<DatabaseDiProva> CreaAsync(CancellationToken annullamento = default)
    {
        var connessioneServer = ConfigurazioneProva.RichiediConnessioneServer();
        var nome = $"DvCmsTest_{Guid.NewGuid():N}";

        await EseguiAsync(connessioneServer, $"CREATE DATABASE [{nome}]", annullamento);

        var costruttore = new SqlConnectionStringBuilder(connessioneServer) { InitialCatalog = nome };
        return new DatabaseDiProva(connessioneServer, nome, costruttore.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await EseguiAsync(
            _connessioneServer,
            $"IF DB_ID(N'{Nome}') IS NOT NULL BEGIN ALTER DATABASE [{Nome}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Nome}]; END",
            CancellationToken.None);
    }

    /// <summary>Esegue un'istruzione sul server (non sul database temporaneo).</summary>
    public static async Task EseguiAsync(string connessione, string istruzione, CancellationToken annullamento)
    {
        await using var connessioneSql = new SqlConnection(connessione);
        await connessioneSql.OpenAsync(annullamento);
        await using var comando = connessioneSql.CreateCommand();
#pragma warning disable CA2100 // Le istruzioni sono costruite dai test con nomi generati localmente, mai con input esterno.
        comando.CommandText = istruzione;
#pragma warning restore CA2100
        await comando.ExecuteNonQueryAsync(annullamento);
    }

    /// <summary>Esegue una query scalare sul database temporaneo.</summary>
    public async Task<T?> LeggiScalareAsync<T>(string query, CancellationToken annullamento = default)
    {
        await using var connessioneSql = new SqlConnection(Connessione);
        await connessioneSql.OpenAsync(annullamento);
        await using var comando = connessioneSql.CreateCommand();
#pragma warning disable CA2100
        comando.CommandText = query;
#pragma warning restore CA2100
        var risultato = await comando.ExecuteScalarAsync(annullamento);
        return risultato is null or DBNull ? default : (T)Convert.ChangeType(risultato, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>
/// Avvia l'applicazione web per i test con una stringa di connessione esplicita.
/// </summary>
public abstract class FabbricaApplicazione : WebApplicationFactory<Program>
{
    private readonly string _connessione;

    protected FabbricaApplicazione(string connessione)
    {
        _connessione = connessione;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:Predefinita", _connessione);
    }
}

/// <summary>
/// Per i test che non toccano il database: la stringa di connessione è un segnaposto che non apre connessioni.
/// Utilizzabile come fixture di xUnit.
/// </summary>
public sealed class FabbricaApplicazioneSenzaDatabase : FabbricaApplicazione
{
    public FabbricaApplicazioneSenzaDatabase()
        : base("Server=localhost;Database=DvCmsNonUsato;Integrated Security=false;TrustServerCertificate=True")
    {
    }
}

/// <summary>Per i test che usano una stringa di connessione specifica (anche vuota o non valida).</summary>
public sealed class FabbricaApplicazioneConConnessione : FabbricaApplicazione
{
    public FabbricaApplicazioneConConnessione(string connessione)
        : base(connessione)
    {
    }
}

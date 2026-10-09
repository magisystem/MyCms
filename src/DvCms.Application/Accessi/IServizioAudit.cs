namespace DvCms.Application.Accessi;

/// <summary>Scrittura nel registro di audit (tabella <c>RegistroAudit</c>).</summary>
public interface IServizioAudit
{
    Task RegistraAsync(
        string azione,
        string risorsa,
        string esito,
        Guid? idTenant,
        Guid? idUtenzaAttore,
        string? dettagli,
        CancellationToken annullamento);
}

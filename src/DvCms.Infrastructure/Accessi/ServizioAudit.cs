using DvCms.Contracts.Accessi;
using DvCms.Extender;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models;
using DvCms.Models.Accessi;

namespace DvCms.Infrastructure.Accessi;

public sealed class ServizioAudit : IServizioAudit
{
    private readonly ContestoDatabase _contesto;
    private readonly TimeProvider _orologio;

    public ServizioAudit(ContestoDatabase contesto, TimeProvider orologio)
    {
        _contesto = contesto;
        _orologio = orologio;
    }

    public async Task RegistraAsync(
        string azione,
        string risorsa,
        string esito,
        Guid? idTenant,
        Guid? idUtenzaAttore,
        string? dettagli,
        CancellationToken annullamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(azione);
        ArgumentException.ThrowIfNullOrWhiteSpace(risorsa);
        ArgumentException.ThrowIfNullOrWhiteSpace(esito);

        _contesto.RegistroAudit.Add(new RegistroAudit
        {
            Id = Identificativi.Nuovo(_orologio),
            IdTenant = idTenant,
            IdUtenzaAttore = idUtenzaAttore,
            Azione = azione,
            Risorsa = risorsa.Length > 200 ? risorsa[..200] : risorsa,
            Esito = esito,
            Dettagli = dettagli is { Length: > RegistroAudit.LunghezzaMassimaDettagli }
                ? dettagli[..RegistroAudit.LunghezzaMassimaDettagli]
                : dettagli,
            CreatoIlUtc = _orologio.GetUtcNow().UtcDateTime,
        });

        await _contesto.SaveChangesAsync(annullamento);
    }
}

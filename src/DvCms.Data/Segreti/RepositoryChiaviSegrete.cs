using System.Xml.Linq;
using DvCms.Data;
using DvCms.Extender;
using DvCms.Models.Segreti;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DvCms.Data.Segreti;

/// <summary>Archivio delle chiavi di protezione dei segreti: righe della tabella <c>ChiaviSegrete</c>, ciascuna già cifrata con la chiave maestra.</summary>
public sealed class RepositoryChiaviSegrete : IXmlRepository
{
    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly TimeProvider _orologio;

    public RepositoryChiaviSegrete(IServiceScopeFactory fabbricaAmbiti, TimeProvider orologio)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _orologio = orologio;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var ambito = _fabbricaAmbiti.CreateScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        return [.. contesto.ChiaviSegrete.AsNoTracking().OrderBy(chiave => chiave.CreatoIlUtc).Select(chiave => chiave.Xml).AsEnumerable().Select(XElement.Parse)];
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var ambito = _fabbricaAmbiti.CreateScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        contesto.ChiaviSegrete.Add(new ChiaveSegreta
        {
            Id = Identificativi.Nuovo(_orologio),
            NomeAmichevole = friendlyName.Length > 200 ? friendlyName[..200] : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            CreatoIlUtc = _orologio.GetUtcNow().UtcDateTime,
        });
        contesto.SaveChanges();
    }
}

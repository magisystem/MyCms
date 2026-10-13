using DvCms.Core.Tenancy;
using DvCms.DTO.Tenancy;
using Xunit;

namespace DvCms.UnitTests.Tenancy;

public class ContestoTenantTest
{
    private static DatiTenantRisolto Dati(Guid? idTenant = null) =>
        new(idTenant ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cms.esempio.it", TenantPrimario: false);

    [Fact]
    public void PartePrivoDiTenant()
    {
        var contesto = new ContestoTenant();

        Assert.False(contesto.Risolto);
        Assert.Null(contesto.IdTenantOpzionale);
        Assert.Throws<InvalidOperationException>(() => contesto.IdTenant);
        Assert.Throws<InvalidOperationException>(() => contesto.IdSito);
    }

    [Fact]
    public void DopoLImpostazioneEspoleIDati()
    {
        var contesto = new ContestoTenant();
        var dati = Dati();

        contesto.Imposta(dati);

        Assert.True(contesto.Risolto);
        Assert.Equal(dati.IdTenant, contesto.IdTenant);
        Assert.Equal(dati.IdTenant, contesto.IdTenantOpzionale);
        Assert.Equal(dati.IdSito, contesto.IdSito);
        Assert.Equal("cms.esempio.it", contesto.Host);
    }

    [Fact]
    public void IlTenantNonSiPuoSostituire()
    {
        var contesto = new ContestoTenant();
        contesto.Imposta(Dati());

        Assert.Throws<InvalidOperationException>(() => contesto.Imposta(Dati()));
    }
}

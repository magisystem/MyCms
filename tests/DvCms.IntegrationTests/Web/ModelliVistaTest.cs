using DvCms.Core.Cms;
using DvCms.Web.Pubblico;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>
/// Ogni versione di modello rilasciata deve avere la sua vista: se manca, <see cref="ModelliVista"/> ripiega in silenzio sul modello
/// semplice e la pagina pubblicata perderebbe il contenuto (accaduto aggiungendo una versione di template senza la vista).
/// </summary>
public class ModelliVistaTest
{
    [Fact]
    public void OgniModelloRilasciatoHaLaSuaVista()
    {
        var ripiego = ModelliVista.Vista("modello-inesistente", 1);

        foreach (var modello in RegistroModelliPagina.Predefiniti)
        {
            var vista = ModelliVista.Vista(modello.Id, modello.Versione);
            if (modello.Id != "testo-semplice")
            {
                Assert.True(vista != ripiego, $"Nessuna vista per {modello.Id} v{modello.Versione}: aggiungerla in ModelliVista.");
            }
        }
    }
}

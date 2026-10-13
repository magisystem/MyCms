using System.Security.Cryptography;
using System.Xml.Linq;
using DvCms.Application.Email;
using DvCms.Extender;
using DvCms.Infrastructure.Persistenza;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DvCms.Infrastructure.Segreti;

/// <summary>
/// Chiave maestra dei segreti, letta dalla configurazione di ambiente (<c>Sicurezza:ChiaveSegreti</c>, 32 byte in base64,
/// ad esempio da <c>openssl rand -base64 32</c>). Se manca o non è valida, i segreti non si possono né cifrare né leggere.
/// </summary>
public sealed class ChiaveMaestraSegreti
{
    public const string NomeConfigurazione = "Sicurezza:ChiaveSegreti";

    private readonly string? _configurata;

    public ChiaveMaestraSegreti(string? configurata)
    {
        _configurata = configurata;
    }

    public byte[] Valore
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_configurata))
            {
                throw new InvalidOperationException(
                    $"Chiave dei segreti mancante: impostare '{NomeConfigurazione}' (32 byte in base64, ad esempio con 'openssl rand -base64 32') " +
                    "tramite user-secrets o variabile d'ambiente Sicurezza__ChiaveSegreti.");
            }

            try
            {
                var chiave = Convert.FromBase64String(_configurata.Trim());
                if (chiave.Length == 32)
                {
                    return chiave;
                }
            }
            catch (FormatException)
            {
                // gestito sotto con lo stesso messaggio, senza ripetere il valore
            }

            throw new InvalidOperationException($"'{NomeConfigurazione}' non è valida: attesi 32 byte in base64.");
        }
    }
}

/// <summary>
/// Protezione dei segreti dei tenant con ASP.NET Core Data Protection (ADR-0015), in un'istanza dedicata e separata da
/// quella di cookie e antiforgery: il key ring sta nella tabella <c>ChiaviSegrete</c> ed è cifrato con la chiave maestra
/// di ambiente. Le chiavi si ruotano da sole; quelle vecchie restano per leggere i segreti già cifrati.
/// </summary>
public sealed class ProtettoreSegreti : IProtettoreSegreti, IDisposable
{
    private const string Scopo = "DvCms.SegretiTenant.v1";

    private readonly ServiceProvider _fornitoreInterno;
    private readonly Lazy<IDataProtector> _protettore;

    public ProtettoreSegreti(IServiceScopeFactory fabbricaAmbiti, ChiaveMaestraSegreti chiaveMaestra, TimeProvider orologio)
    {
        var servizi = new ServiceCollection();
        servizi.AddSingleton(chiaveMaestra);
        servizi.AddSingleton(orologio);
        servizi.AddSingleton<IXmlRepository>(new RepositoryChiaviSegrete(fabbricaAmbiti, orologio));
        servizi.AddDataProtection().SetApplicationName("DvCms.Segreti");
        servizi.AddSingleton<IConfigureOptions<KeyManagementOptions>>(fornitore =>
            new ConfigureOptions<KeyManagementOptions>(opzioni =>
            {
                opzioni.XmlRepository = fornitore.GetRequiredService<IXmlRepository>();
                opzioni.XmlEncryptor = new CifraturaChiaveMaestra(fornitore.GetRequiredService<ChiaveMaestraSegreti>());
            }));
        _fornitoreInterno = servizi.BuildServiceProvider();
        _protettore = new Lazy<IDataProtector>(
            () => _fornitoreInterno.GetRequiredService<IDataProtectionProvider>().CreateProtector(Scopo),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Proteggi(string testo)
    {
        ArgumentNullException.ThrowIfNull(testo);
        try
        {
            return _protettore.Value.Protect(testo);
        }
        catch (CryptographicException eccezione) when (eccezione.InnerException is InvalidOperationException configurazione)
        {
            // Chiave maestra mancante o non valida: il messaggio indica cosa configurare e non contiene la chiave.
            throw new InvalidOperationException(configurazione.Message, eccezione);
        }
    }

    public string Rivela(string protetto)
    {
        ArgumentException.ThrowIfNullOrEmpty(protetto);
        try
        {
            return _protettore.Value.Unprotect(protetto);
        }
        catch (CryptographicException eccezione) when (eccezione.InnerException is InvalidOperationException configurazione)
        {
            throw new InvalidOperationException(configurazione.Message, eccezione);
        }
        catch (CryptographicException eccezione)
        {
            // Senza dettagli: il messaggio originale non deve trapelare nei log.
            throw new InvalidOperationException("Impossibile leggere il segreto: dato non valido o chiave dei segreti diversa.", eccezione);
        }
    }

    public void Dispose() => _fornitoreInterno.Dispose();
}

internal sealed class RepositoryChiaviSegrete : IXmlRepository
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

/// <summary>Cifra l'XML delle chiavi del key ring con AES-GCM e la chiave maestra di ambiente.</summary>
internal sealed class CifraturaChiaveMaestra : IXmlEncryptor
{
    private readonly ChiaveMaestraSegreti _chiave;

    public CifraturaChiaveMaestra(ChiaveMaestraSegreti chiave)
    {
        _chiave = chiave;
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var chiaro = System.Text.Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cifrato = new byte[chiaro.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(_chiave.Valore, tag.Length))
        {
            aes.Encrypt(nonce, chiaro, cifrato, tag);
        }

        var elemento = new XElement(
            "cifrato",
            new XAttribute("versione", 1),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("dati", Convert.ToBase64String(cifrato)));
        return new EncryptedXmlInfo(elemento, typeof(DecifraturaChiaveMaestra));
    }
}

/// <summary>Istanziata da Data Protection con i servizi interni (vedi <see cref="ProtettoreSegreti"/>).</summary>
internal sealed class DecifraturaChiaveMaestra : IXmlDecryptor
{
    private readonly ChiaveMaestraSegreti _chiave;

    public DecifraturaChiaveMaestra(IServiceProvider servizi)
    {
        _chiave = servizi.GetRequiredService<ChiaveMaestraSegreti>();
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var nonce = Convert.FromBase64String((string)encryptedElement.Element("nonce")!);
        var tag = Convert.FromBase64String((string)encryptedElement.Element("tag")!);
        var cifrato = Convert.FromBase64String((string)encryptedElement.Element("dati")!);
        var chiaro = new byte[cifrato.Length];
        using (var aes = new AesGcm(_chiave.Valore, tag.Length))
        {
            aes.Decrypt(nonce, cifrato, tag, chiaro);
        }

        return XElement.Parse(System.Text.Encoding.UTF8.GetString(chiaro));
    }
}

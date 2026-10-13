using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Email;

namespace DvCms.DTO.Email;

public enum TipoCampoEmail
{
    Testo = 1,
    Numero = 2,
    Email = 3,
    ModalitaTls = 4,
    TestoLungo = 5,

    /// <summary>Identificativo senza spazi né simboli speciali (tenant Microsoft, client id): finisce in indirizzi, quindi si limita l'alfabeto.</summary>
    Identificativo = 6,

    /// <summary>JSON di un account di servizio Google (<c>type</c>, <c>client_email</c>, <c>private_key</c>).</summary>
    CredenzialiGoogle = 7,

    /// <summary>Data nel formato <c>aaaa-mm-gg</c>.</summary>
    Data = 8,
}

/// <summary>Un campo di configurazione di un canale: la UI mostra solo i campi del canale scelto.</summary>
public sealed record CampoProviderEmail(string Nome, string Etichetta, TipoCampoEmail Tipo, bool Obbligatorio, bool Segreto);

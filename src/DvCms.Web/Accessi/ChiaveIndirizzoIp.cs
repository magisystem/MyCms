using System.Net;
using System.Net.Sockets;

namespace DvCms.Web.Accessi;

/// <summary>
/// Chiave con cui si conta un client nei limiti di richieste (accesso, recupero, inviti). Per IPv4 è l'indirizzo; per IPv6 è la
/// rete /64, perché chi ha un indirizzo IPv6 domestico o di un server dispone di un intero /64 e potrebbe ruotare gli indirizzi
/// per ottenere un limite nuovo a ogni richiesta. Un IPv4 incapsulato in IPv6 vale come IPv4.
/// </summary>
public static class ChiaveIndirizzoIp
{
    public const string Sconosciuto = "sconosciuto";

    public static string Calcola(IPAddress? indirizzo)
    {
        if (indirizzo is null)
        {
            return Sconosciuto;
        }

        if (indirizzo.IsIPv4MappedToIPv6)
        {
            indirizzo = indirizzo.MapToIPv4();
        }

        if (indirizzo.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return indirizzo.ToString();
        }

        var byteRete = indirizzo.GetAddressBytes();
        Array.Clear(byteRete, 8, 8);
        return new IPAddress(byteRete) + "/64";
    }
}

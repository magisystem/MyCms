namespace DvCms.Contracts.Localizzazione;

/// <summary>Lingua corrente dell'operazione (richiesta web o processo in background).</summary>
public interface IContestoLingua
{
    string CodiceLingua { get; }
}

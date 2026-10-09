namespace DvCms.Application.Localizzazione;

/// <summary>Lingua corrente dell'operazione (richiesta web o processo in background).</summary>
public interface IContestoLingua
{
    string CodiceLingua { get; }
}

/// <summary>Contesto con lingua fissa; l'italiano è la lingua predefinita.</summary>
public sealed class ContestoLinguaFisso : IContestoLingua
{
    public ContestoLinguaFisso(string codiceLingua = Domain.Localizzazione.Lingua.CodiceItaliano)
    {
        CodiceLingua = codiceLingua;
    }

    public string CodiceLingua { get; }
}

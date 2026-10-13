using DvCms.Contracts.Localizzazione;

namespace DvCms.Core.Localizzazione;

/// <summary>Contesto con lingua fissa; l'italiano è la lingua predefinita.</summary>
public sealed class ContestoLinguaFisso : IContestoLingua
{
    public ContestoLinguaFisso(string codiceLingua = Models.Localizzazione.Lingua.CodiceItaliano)
    {
        CodiceLingua = codiceLingua;
    }

    public string CodiceLingua { get; }
}

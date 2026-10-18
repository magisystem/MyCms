using DvCms.DTO.Moduli;

namespace DvCms.Web.Pubblico;

/// <summary>
/// Ciò che serve a mostrare un modulo di contatto (ADR-0027): la definizione, la lingua del visitatore, i valori già scritti e gli errori
/// per campo dopo un invio rifiutato. Con <see cref="Anteprima"/> il pulsante è spento (anteprima nell'admin).
/// </summary>
public sealed record ModelloFormModulo(
    Guid? IdModulo,
    DefinizioneModulo Definizione,
    string Lingua,
    string LinguaPredefinita,
    bool Anteprima,
    string? Azione,
    bool ConProvaAntiAbuso,
    IReadOnlyDictionary<string, string[]>? Valori = null,
    IReadOnlyDictionary<string, string>? Errori = null,
    string? ErroreGenerale = null)
{
    public string[] ValoriDi(string idCampo) => Valori is not null && Valori.TryGetValue(idCampo, out var valori) ? valori : [];

    public string? ErroreDi(string idCampo) => Errori is not null && Errori.TryGetValue(idCampo, out var errore) ? errore : null;
}

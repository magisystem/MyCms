using DvCms.DTO.Piattaforma;

namespace DvCms.DTO.Builder;

/// <summary>Stato del builder per una pagina: il progetto da aprire nell'editor, i vincoli del template e il token di concorrenza.</summary>
public sealed record StatoBuilder(
    Guid IdPagina,
    string Titolo,
    string CodiceLingua,
    string IdTemplate,
    int VersioneTemplate,
    int NumeroRevisione,
    string ProgettoEditorJson,
    string TokenConcorrenza,
    string CatalogoJson,
    string VincoliJson);

public sealed record EsitoSalvataggioBuilder(
    CodiceEsito Codice,
    IReadOnlyList<ErroreBuilder> Errori,
    string? TokenConcorrenza = null,
    string? ProgettoEditorJson = null,
    int NumeroRevisione = 0,
    bool NuovaRevisione = false)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

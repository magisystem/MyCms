namespace DvCms.Web.Pubblico;

/// <summary>
/// Associa ogni versione di modello di pagina alla vista che la disegna. Una revisione si disegna con la vista della
/// versione con cui è stata scritta; se la vista manca (non dovrebbe) si ripiega sul modello semplice.
/// </summary>
public static class ModelliVista
{
    private static readonly Dictionary<(string Id, int Versione), string> _viste = new()
    {
        [("testo-semplice", 1)] = "Modelli/_testo-semplice_v1",
        [("presentazione", 1)] = "Modelli/_presentazione_v1",
    };

    public static string Vista(string id, int versione) =>
        _viste.TryGetValue((id, versione), out var vista) ? vista : _viste[("testo-semplice", 1)];
}

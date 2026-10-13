using DvCms.Contracts.Localizzazione;

namespace DvCms.Web.Cms;

/// <summary>Testi localizzati dei temi e delle loro impostazioni (i nomi tecnici restano nel registro).</summary>
public static class EtichetteTemi
{
    public static string NomeTema(ILocalizzatore localizzatore, string chiave)
    {
        ArgumentNullException.ThrowIfNull(localizzatore);

        return chiave switch
        {
            "classico" => localizzatore.Traduci("Classico"),
            "moderno" => localizzatore.Traduci("Moderno"),
            _ => chiave,
        };
    }

    public static string Campo(ILocalizzatore localizzatore, string nomeCampo)
    {
        ArgumentNullException.ThrowIfNull(localizzatore);

        return nomeCampo switch
        {
            "ColorePrimario" => localizzatore.Traduci("Colore principale"),
            "ColoreIntestazione" => localizzatore.Traduci("Colore dell'intestazione"),
            "Larghezza" => localizzatore.Traduci("Larghezza del contenuto"),
            "TestoPiePagina" => localizzatore.Traduci("Testo del piè di pagina"),
            _ => nomeCampo,
        };
    }

    public static string Valore(ILocalizzatore localizzatore, string nomeCampo, string valore)
    {
        ArgumentNullException.ThrowIfNull(localizzatore);

        return (nomeCampo, valore) switch
        {
            ("Larghezza", "Stretta") => localizzatore.Traduci("Stretta"),
            ("Larghezza", "Larga") => localizzatore.Traduci("Larga"),
            _ => valore,
        };
    }
}

using DvCms.Application.Localizzazione;

namespace DvCms.Web.Cms;

/// <summary>Testi localizzati dei modelli di pagina e dei loro campi (i nomi tecnici restano nel registro).</summary>
public static class EtichetteModelli
{
    public static string NomeModello(ILocalizzatore localizzatore, string idModello)
    {
        ArgumentNullException.ThrowIfNull(localizzatore);

        return idModello switch
        {
            "testo-semplice" => localizzatore.Traduci("Testo semplice"),
            "presentazione" => localizzatore.Traduci("Presentazione con pulsante"),
            "pagina-libera" => localizzatore.Traduci("Pagina libera (page builder)"),
            "pagina-con-intestazione-fissa" => localizzatore.Traduci("Pagina con intestazione fissa (page builder)"),
            _ => idModello,
        };
    }

    public static string Campo(ILocalizzatore localizzatore, string nomeCampo)
    {
        ArgumentNullException.ThrowIfNull(localizzatore);

        return nomeCampo switch
        {
            "Sottotitolo" => localizzatore.Traduci("Sottotitolo"),
            "EtichettaPulsante" => localizzatore.Traduci("Testo del pulsante"),
            "UrlPulsante" => localizzatore.Traduci("Indirizzo del pulsante"),
            "Contenuto" => localizzatore.Traduci("Contenuto"),
            "Modello" => localizzatore.Traduci("Modello"),
            _ => nomeCampo,
        };
    }
}

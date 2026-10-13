namespace DvCms.DTO.Media;

/// <summary>Tipo e dimensioni di un'immagine riconosciuta dalla sua firma.</summary>
public sealed record ImmagineRilevata(string TipoContenuto, string Estensione, int Larghezza, int Altezza);

namespace DvCms.Domain.Negozio;

/// <summary>
/// Categoria (o collezione) di prodotti del sito: un raggruppamento piatto, con nome per lingua, che il cliente usa per filtrare la
/// vetrina. Un prodotto può stare in più categorie. Una categoria vuota non si mostra in vetrina.
/// </summary>
public class Categoria
{
    public const int NumeroMassimo = 50;
    public const int LunghezzaMassimaNome = 100;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    /// <summary>Ordine di presentazione (crescente); a parità si ordina per nome.</summary>
    public int Posizione { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }
}

public class TraduzioneCategoria
{
    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdCategoria { get; set; }

    public Guid IdLingua { get; set; }

    public string Nome { get; set; } = string.Empty;
}

/// <summary>Appartenenza di un prodotto a una categoria.</summary>
public class ProdottoCategoria
{
    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdProdotto { get; set; }

    public Guid IdCategoria { get; set; }
}

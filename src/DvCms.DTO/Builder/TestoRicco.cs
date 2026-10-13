using System.Text;

namespace DvCms.DTO.Builder;

/// <summary>Un tratto di testo con la sua formattazione, un a capo oppure un collegamento (con testo semplice).</summary>
public abstract record InlineTestoRicco
{
    public sealed record Testo(string Valore, bool Grassetto, bool Corsivo) : InlineTestoRicco;

    public sealed record ACapo : InlineTestoRicco;

    public sealed record Collegamento(string Etichetta, string Indirizzo) : InlineTestoRicco;
}

/// <summary>Un blocco: un paragrafo oppure un elenco puntato.</summary>
public abstract record BloccoTestoRicco
{
    public sealed record Paragrafo(IReadOnlyList<InlineTestoRicco> Contenuto) : BloccoTestoRicco;

    public sealed record Elenco(IReadOnlyList<IReadOnlyList<InlineTestoRicco>> Voci) : BloccoTestoRicco;
}

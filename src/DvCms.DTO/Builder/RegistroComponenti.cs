using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DvCms.DTO.Builder;

public enum TipoProprieta
{
    TestoBreve = 1,
    TestoLungo = 2,
    Url = 3,
    Enumerazione = 4,

    /// <summary>Identificativo (GUID) di una risorsa della libreria media; l'esistenza nel sito la verifica il servizio.</summary>
    IdRisorsa = 5,

    /// <summary>"si" oppure "no".</summary>
    Booleano = 6,

    /// <summary>Testo con formattazione in markup a grammatica chiusa (<see cref="Builder.TestoRicco"/>), memorizzato in forma canonica.</summary>
    TestoRicco = 7,

    /// <summary>Identificativo (GUID) di un prodotto del catalogo; l'esistenza nel sito la verifica il servizio, la pubblicazione la verifica il renderer a ogni visita.</summary>
    IdProdotto = 8,

    /// <summary>Identificativo (GUID) di una categoria del negozio; l'esistenza nel sito la verifica il servizio, i prodotti visibili il renderer a ogni visita.</summary>
    IdCategoria = 9,

    /// <summary>Identificativo (GUID) di un modulo di contatto del sito (ADR-0027); l'esistenza nel sito la verifica il servizio, la pubblicazione il renderer a ogni visita.</summary>
    IdModulo = 10,
}

/// <summary>Una proprietà (o uno stile) ammessa da un componente. Gli stili sono sempre enumerazioni: token e varianti, mai CSS libero.</summary>
public sealed record DefinizioneProprieta(
    string Nome,
    TipoProprieta Tipo,
    bool Obbligatoria = false,
    int LunghezzaMassima = 0,
    IReadOnlyList<string>? Valori = null,
    string Predefinito = "");

/// <summary>Un componente del catalogo: ciò che accetta, cosa può contenere e cosa può fare l'editor con esso.</summary>
public sealed record DefinizioneComponente(
    string Tipo,
    int VersioneSchema,
    IReadOnlyList<DefinizioneProprieta> Proprieta,
    IReadOnlyList<DefinizioneProprieta> Stili,
    IReadOnlyList<string> FigliAmmessi,
    int FigliMinimi = 0,
    int FigliMassimi = 0)
{
    public bool Contenitore => FigliAmmessi.Count > 0;
}

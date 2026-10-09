namespace DvCms.Application.Localizzazione;

/// <summary>
/// Localizzazione dei testi statici (ADR-0018). Il primo argomento è il testo italiano di base e deve essere
/// <b>sempre una costante letterale</b>: mai dati degli utenti o dei visitatori (un test lo verifica).
/// Nelle viste Razor si usa come <c>@_L["TESTO"]</c>.
/// </summary>
public interface ILocalizzatore
{
    /// <summary>Come <see cref="Traduci"/>, nella lingua corrente.</summary>
    string this[string chiave, params object?[] argomenti] { get; }

    /// <summary>Traduce nella lingua corrente della richiesta.</summary>
    string Traduci(string chiave, params object?[] argomenti);

    /// <summary>Traduce in una lingua esplicita (processi in background, email).</summary>
    string TraduciIn(string codiceLingua, string chiave, params object?[] argomenti);
}

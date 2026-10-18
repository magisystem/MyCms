using DvCms.DTO.Moduli;

namespace DvCms.Contracts.Moduli;

/// <summary>
/// Moduli di contatto visti dal sito pubblico (ADR-0027): mostrare un modulo pubblicato e riceverne gli invii. Lavora solo sul tenant e
/// sul sito dell'host; un modulo in bozza, archiviato o di un altro sito non esiste. L'anti-spam (ALTCHA) lo verifica chi chiama,
/// prima di invocare <see cref="InviaAsync"/>.
/// </summary>
public interface IServizioModuloPubblico
{
    /// <summary>Il modulo pubblicato, con la lingua del visitatore (se non è abilitata nel sito, la predefinita); <c>null</c> se non esiste o non è pubblicato.</summary>
    Task<ModuloPubblico?> LeggiAsync(Guid idModulo, string? lingua, CancellationToken annullamento);

    /// <summary>
    /// Verifica i valori contro la definizione corrente e salva la richiesta. <paramref name="valori"/> sono i campi della form
    /// (<c>campo_c1</c>…): quelli non previsti dal modulo si ignorano.
    /// </summary>
    Task<RisultatoInvioModulo> InviaAsync(Guid idModulo, string? lingua, IReadOnlyDictionary<string, string[]> valori, CancellationToken annullamento);
}

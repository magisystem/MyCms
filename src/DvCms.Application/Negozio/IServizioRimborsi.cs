using DvCms.Domain.Negozio;

namespace DvCms.Application.Negozio;

public enum RisultatoRimborso
{
    /// <summary>Rimborsato: il provider ha restituito il denaro.</summary>
    Completato = 1,

    /// <summary>Richiesto ma non ancora concluso (il provider lo sta elaborando o la risposta non è arrivata): l'importo resta impegnato e si può verificare di nuovo.</summary>
    InCorso = 2,

    /// <summary>Il provider ha rifiutato: l'importo torna rimborsabile.</summary>
    Rifiutato = 3,

    DatiNonValidi = 4,

    /// <summary>L'ordine o l'incasso non è in uno stato rimborsabile.</summary>
    NonRimborsabile = 5,

    ErroreProvider = 6,

    NonConfigurato = 7,

    NonTrovato = 8,

    NonAutorizzato = 9,
}

public sealed record RimborsoVista(Guid Id, decimal Importo, string Valuta, string? Motivo, StatoRimborso Stato, bool IncassoOrfano, DateTime CreatoIlUtc);

/// <summary>
/// Rimborsi totali o parziali dal pannello del tenant (A5, ADR-0020), eseguiti dal provider con richieste idempotenti. L'importo di
/// un rimborso si impegna sull'ordine con un aggiornamento condizionale **prima** di chiamare il provider, quindi richieste
/// concorrenti non possono rimborsare più del totale; se l'esito è incerto l'importo resta impegnato finché non si verifica.
/// Solo <c>TenantAdmin</c> del tenant o <c>PlatformAdmin</c>, ruolo riverificato a ogni operazione.
/// </summary>
public interface IServizioRimborsi
{
    /// <param name="importo">Quanto rimborsare (IVA inclusa, al più due decimali); <c>null</c> = tutto ciò che resta.</param>
    /// <param name="motivo">Facoltativo, al più 200 caratteri; si comunica anche al cliente.</param>
    Task<RisultatoRimborso> RimborsaOrdineAsync(Guid idAttore, Guid idOrdine, decimal? importo, string? motivo, CancellationToken annullamento);

    /// <summary>Restituisce un incasso che non è diventato un ordine pagato (tentativo «da rimborsare»), per intero.</summary>
    Task<RisultatoRimborso> RimborsaIncassoAsync(Guid idAttore, Guid idTentativo, string? motivo, CancellationToken annullamento);

    /// <summary>Ripete la richiesta di un rimborso rimasto in corso (stesso identificativo: il provider non rimborsa due volte) e ne applica l'esito.</summary>
    Task<RisultatoRimborso> VerificaAsync(Guid idAttore, Guid idRimborso, CancellationToken annullamento);

    /// <summary>Per un webhook: rilegge dal provider il rimborso con quell'identificativo e ne applica l'esito. Falso se il negozio non lo conosce.</summary>
    Task<bool> AllineaDaProviderAsync(string idRimborsoProvider, CancellationToken annullamento);

    Task<IReadOnlyList<RimborsoVista>> ElencaAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);
}

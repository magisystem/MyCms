using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;

namespace DvCms.Contracts.Negozio;

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

    /// <summary>
    /// Per un webhook PayPal di rimborso completato che il negozio non conosce (fatto dal pannello PayPal): lo rilegge dal provider e lo
    /// registra come rimborso «esterno», aggiornando l'ordine. Vero se il rimborso è ora noto (registrato adesso o già prima); falso se non si riferisce
    /// a un incasso di questo sito.
    /// </summary>
    Task<bool> RegistraRimborsoEsternoPayPalAsync(string idRimborsoProvider, CancellationToken annullamento);

    /// <summary>
    /// Per una notifica di rimborso di un provider che non li notifica uno a uno (Nexi): rilegge dal provider i rimborsi del pagamento e registra
    /// come «esterni» quelli che il negozio non conosce. Restituisce quanti ne ha registrati.
    /// </summary>
    Task<int> RegistraRimborsiEsterniAsync(Guid idTentativo, CancellationToken annullamento);
}

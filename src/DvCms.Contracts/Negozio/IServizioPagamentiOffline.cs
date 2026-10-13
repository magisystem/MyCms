using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Negozio;

namespace DvCms.Contracts.Negozio;

/// <summary>
/// Pagamenti manuali del sito (ADR-0023). Li configurano il <c>TenantAdmin</c> del tenant (o il <c>PlatformAdmin</c> che vi entra), con il
/// ruolo riverificato a ogni operazione e solo con il negozio attivo; chi può ordinare legge soltanto quelli attivi.
/// </summary>
public interface IServizioPagamentiOffline
{
    /// <summary>Amministrazione: le impostazioni complete (tutto spento se mai salvate).</summary>
    Task<SchedaPagamentoOffline> LeggiAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>
    /// Salva le impostazioni. <c>DatiNonValidi</c> (con i campi: IBAN non valido o mancante con il bonifico attivo, commissione fuori limiti,
    /// minimo oltre il massimo…), <c>NonConsentito</c> (negozio non attivo), <c>Conflitto</c> (modificate da un altro nel frattempo).
    /// </summary>
    Task<EsitoOperazione> SalvaAsync(Guid idAttore, ImpostazioniPagamentoOffline dati, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>Cassa e pagina dell'ordine: le impostazioni di ciò che è attivo (un metodo spento non mostra nulla di sé), per un account che può ordinare.</summary>
    Task<ImpostazioniPagamentoOffline> LeggiPerClienteAsync(Guid idAccount, CancellationToken annullamento);
}

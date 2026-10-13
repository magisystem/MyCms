using DvCms.Contracts.Outbox;
using DvCms.DTO.Outbox;
using Microsoft.Extensions.Logging;

namespace DvCms.Services.Cms;

/// <summary>Tipi di evento dell'outbox legati alle pagine.</summary>
public static class TipiEventoPagina
{
    public const string Pubblicata = "PaginaPubblicata";
    public const string Ritirata = "PaginaRitirata";
    public const string Rinominata = "PaginaRinominata";
}

/// <summary>Dati dell'evento di pubblicazione o ritiro di una pagina.</summary>
public sealed record EventoPagina(Guid IdPagina, Guid IdSito, string Slug);

/// <summary>Dati dell'evento di cambio di slug di una pagina pubblicata.</summary>
public sealed record EventoPaginaRinominata(Guid IdPagina, Guid IdSito, string SlugPrecedente, string SlugNuovo);

/// <summary>
/// Reagisce alla pubblicazione di una pagina. Per ora registra soltanto l'evento: la cache delle pagine pubbliche
/// non esiste ancora e la sua invalidazione si collega qui con G-002. L'operazione è idempotente per natura.
/// </summary>
public sealed partial class GestorePaginaPubblicata : IGestoreOutbox
{
    private readonly ILogger<GestorePaginaPubblicata> _logger;

    public GestorePaginaPubblicata(ILogger<GestorePaginaPubblicata> logger)
    {
        _logger = logger;
    }

    public string Tipo => TipiEventoPagina.Pubblicata;

    public Task GestisciAsync(MessaggioDaElaborare messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(messaggio);
        RegistraEvento(_logger, messaggio.IdTenant, messaggio.Id);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pagina pubblicata (tenant {IdTenant}, messaggio {IdMessaggio}): nessuna cache da invalidare")]
    private static partial void RegistraEvento(ILogger logger, Guid idTenant, Guid idMessaggio);
}

/// <summary>Come <see cref="GestorePaginaPubblicata"/>, per il ritiro di una pagina.</summary>
public sealed partial class GestorePaginaRitirata : IGestoreOutbox
{
    private readonly ILogger<GestorePaginaRitirata> _logger;

    public GestorePaginaRitirata(ILogger<GestorePaginaRitirata> logger)
    {
        _logger = logger;
    }

    public string Tipo => TipiEventoPagina.Ritirata;

    public Task GestisciAsync(MessaggioDaElaborare messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(messaggio);
        RegistraEvento(_logger, messaggio.IdTenant, messaggio.Id);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pagina ritirata (tenant {IdTenant}, messaggio {IdMessaggio}): nessuna cache da invalidare")]
    private static partial void RegistraEvento(ILogger logger, Guid idTenant, Guid idMessaggio);
}

/// <summary>Come <see cref="GestorePaginaPubblicata"/>, per il cambio di slug di una pagina pubblicata (il vecchio indirizzo reindirizza già).</summary>
public sealed partial class GestorePaginaRinominata : IGestoreOutbox
{
    private readonly ILogger<GestorePaginaRinominata> _logger;

    public GestorePaginaRinominata(ILogger<GestorePaginaRinominata> logger)
    {
        _logger = logger;
    }

    public string Tipo => TipiEventoPagina.Rinominata;

    public Task GestisciAsync(MessaggioDaElaborare messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(messaggio);
        RegistraEvento(_logger, messaggio.IdTenant, messaggio.Id);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pagina rinominata (tenant {IdTenant}, messaggio {IdMessaggio}): nessuna cache da invalidare")]
    private static partial void RegistraEvento(ILogger logger, Guid idTenant, Guid idMessaggio);
}

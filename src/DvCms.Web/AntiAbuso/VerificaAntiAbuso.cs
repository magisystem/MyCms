using Altcha.AspNetCore;
using Microsoft.Extensions.Options;

namespace DvCms.Web.AntiAbuso;

/// <summary>
/// Protezione delle form pubbliche dall'invio automatico (ADR-0026). Le pagine chiamano <see cref="VerificaAsync"/> prima di fare
/// qualunque cosa con i dati ricevuti; se la protezione è spenta risponde sempre di sì.
/// </summary>
public interface IVerificaAntiAbuso
{
    /// <summary>Vero se la protezione è accesa: la pagina mostra il widget solo in questo caso.</summary>
    bool Attiva { get; }

    /// <summary>Vero se la richiesta porta una prova valida e mai usata prima.</summary>
    Task<bool> VerificaAsync(HttpRequest richiesta, CancellationToken annullamento);
}

/// <summary>Protezione spenta (<c>Altcha:Abilitato</c> falso): nessuna prova richiesta.</summary>
public sealed class VerificaDisabilitata : IVerificaAntiAbuso
{
    public bool Attiva => false;

    public Task<bool> VerificaAsync(HttpRequest richiesta, CancellationToken annullamento) => Task.FromResult(true);
}

/// <summary>
/// Verifica la prova di lavoro ALTCHA inviata dal widget: firma della sfida, scadenza, soluzione e uso unico. Una prova mancante,
/// scaduta, alterata o già usata respinge la richiesta; nel registro finisce solo il codice dell'errore, mai la prova.
/// </summary>
public sealed partial class VerificaAltcha : IVerificaAntiAbuso
{
    private readonly IAltchaService _altcha;
    private readonly IOptionsMonitor<AltchaOptions> _opzioni;
    private readonly ILogger<VerificaAltcha> _logger;

    public VerificaAltcha(IAltchaService altcha, IOptionsMonitor<AltchaOptions> opzioni, ILogger<VerificaAltcha> logger)
    {
        _altcha = altcha;
        _opzioni = opzioni;
        _logger = logger;
    }

    public bool Attiva => true;

    public async Task<bool> VerificaAsync(HttpRequest richiesta, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(richiesta);

        if (!richiesta.HasFormContentType)
        {
            return false;
        }

        var modulo = await richiesta.ReadFormAsync(annullamento);
        var esito = await _altcha.VerifyAsync(
            modulo[_opzioni.CurrentValue.FieldName].FirstOrDefault(),
            nome => modulo[nome].FirstOrDefault(),
            annullamento);
        if (!esito.Verified)
        {
            RegistraRifiuto(_logger, esito.ErrorCode, richiesta.Path);
        }

        return esito.Verified;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Prova anti-abuso rifiutata ({Codice}) su {Percorso}")]
    private static partial void RegistraRifiuto(ILogger logger, AltchaErrorCode? codice, PathString percorso);
}

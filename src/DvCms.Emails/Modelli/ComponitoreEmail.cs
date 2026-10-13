using DvCms.Contracts.Email;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.Models.Localizzazione;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DvCms.Emails;

/// <summary>Compone le email dai modelli (ADR-0025): tenant e lingua, tenant e lingua predefinita, fabbrica e lingua, fabbrica in italiano.</summary>
public sealed partial class ComponitoreEmail : IComponitoreEmail
{
    private readonly ContestoDatabase _contesto;
    private readonly ILogger<ComponitoreEmail> _logger;

    public ComponitoreEmail(ContestoDatabase contesto, ILogger<ComponitoreEmail> logger)
    {
        _contesto = contesto;
        _logger = logger;
    }

    public async Task<MessaggioEmail> ComponiAsync(
        Guid idTenant,
        string codice,
        string lingua,
        string destinatario,
        string? nomeDestinatario,
        IReadOnlyDictionary<string, object?> variabili,
        CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(variabili);

        if (CatalogoModelliEmail.Trova(codice) is null)
        {
            throw new ArgumentException($"Modello email sconosciuto: {codice}.", nameof(codice));
        }

        var richiesta = NormalizzaLingua(lingua);
        var predefinita = await LinguaPredefinitaDelTenantAsync(idTenant, annullamento);
        var righe = await _contesto.ModelliEmail.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IdTenant == idTenant && m.Codice == codice && (m.Lingua == richiesta || m.Lingua == predefinita))
            .ToListAsync(annullamento);

        foreach (var codiceLingua in new[] { richiesta, predefinita }.Distinct(StringComparer.Ordinal))
        {
            var riga = righe.FirstOrDefault(m => m.Lingua == codiceLingua);
            if (riga is null)
            {
                continue;
            }

            try
            {
                return await RendiAsync(new ContenutoModelloEmail(riga.Oggetto, riga.CorpoHtml, riga.CorpoTesto), destinatario, nomeDestinatario, variabili);
            }
            catch (Exception eccezione) when (eccezione is InvalidOperationException or FormatException)
            {
                // Un modello del tenant non più valido non deve fermare l'invio: si ripiega sul modello di fabbrica.
                RegistraModelloNonRenderizzabile(_logger, codice, codiceLingua, eccezione.GetType().Name);
            }
        }

        var fabbrica = CatalogoModelliEmail.Fabbrica(codice, richiesta)
            ?? CatalogoModelliEmail.Fabbrica(codice, Lingua.CodiceItaliano)
            ?? throw new InvalidOperationException($"Manca il modello di fabbrica italiano per {codice}.");
        return await RendiAsync(fabbrica, destinatario, nomeDestinatario, variabili);
    }

    internal static string NormalizzaLingua(string? lingua) =>
        string.IsNullOrWhiteSpace(lingua) ? Lingua.CodiceItaliano : lingua.Trim().ToLowerInvariant();

    private static async Task<MessaggioEmail> RendiAsync(ContenutoModelloEmail contenuto, string destinatario, string? nomeDestinatario, IReadOnlyDictionary<string, object?> variabili)
    {
        var oggetto = await MotoreModelliEmail.RenderizzaOggettoAsync(contenuto.Oggetto, variabili);
        var html = await MotoreModelliEmail.RenderizzaAsync(contenuto.CorpoHtml, variabili, html: true);
        var testo = await MotoreModelliEmail.RenderizzaAsync(contenuto.CorpoTesto, variabili, html: false);
        return new MessaggioEmail(destinatario, nomeDestinatario, oggetto, testo, html);
    }

    /// <summary>La lingua predefinita del tenant è quella predefinita del suo sito più antico; senza siti, l'italiano.</summary>
    private async Task<string> LinguaPredefinitaDelTenantAsync(Guid idTenant, CancellationToken annullamento)
    {
        var codice = await (
            from ls in _contesto.LingueSito.IgnoreQueryFilters().AsNoTracking()
            join sito in _contesto.Siti.IgnoreQueryFilters().AsNoTracking() on ls.IdSito equals sito.Id
            join lingua in _contesto.Lingue.AsNoTracking() on ls.IdLingua equals lingua.Id
            where ls.IdTenant == idTenant && ls.Predefinita && lingua.Attiva
            orderby sito.CreatoIlUtc
            select lingua.Codice)
            .FirstOrDefaultAsync(annullamento);
        return NormalizzaLingua(codice);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Il modello email del tenant non si lascia rendere, si usa quello di fabbrica: {Codice} ({Lingua}), {Tipo}")]
    private static partial void RegistraModelloNonRenderizzabile(ILogger logger, string codice, string lingua, string tipo);
}

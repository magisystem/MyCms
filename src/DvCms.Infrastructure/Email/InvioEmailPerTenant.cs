using System.Text.Json;
using DvCms.Application.Email;
using DvCms.Domain.Accessi;
using DvCms.Domain.Email;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Email;

/// <summary>
/// Implementazione di <see cref="IInvioEmail"/>: legge le impostazioni del tenant (caso d'uso privilegiato, quindi
/// <c>IgnoreQueryFilters</c> con il tenant del messaggio), ne decifra i segreti, le valida e le passa all'adapter del canale.
/// Un tenant senza configurazione valida non invia: le eccezioni non contengono valori delle impostazioni.
/// </summary>
public sealed class InvioEmailPerTenant : IInvioEmail
{
    private readonly ContestoDatabase _contesto;
    private readonly IProtettoreSegreti _protettore;
    private readonly Dictionary<ProviderEmail, IAdapterEmail> _adapter;

    public InvioEmailPerTenant(ContestoDatabase contesto, IProtettoreSegreti protettore, IEnumerable<IAdapterEmail> adapter)
    {
        _contesto = contesto;
        _protettore = protettore;
        _adapter = adapter.ToDictionary(candidato => candidato.Provider);
    }

    public async Task InviaAsync(Guid idTenant, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(messaggio);

        if (!NormalizzatoreEmail.TentaNormalizza(messaggio.Destinatario, out _))
        {
            throw new ConfigurazioneEmailNonValidaException("Indirizzo del destinatario non valido.");
        }

        var riga = await _contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(candidata => candidata.IdTenant == idTenant, annullamento)
            ?? throw new ConfigurazioneEmailNonValidaException("Il tenant non ha impostazioni email.");

        var impostazioni = Leggi(riga);
        var errori = DefinizioniProviderEmail.Valida(impostazioni.Provider, impostazioni.Valori);
        if (errori.Count > 0)
        {
            throw new ConfigurazioneEmailNonValidaException($"Impostazioni email incomplete o non valide: {string.Join(", ", errori)}.");
        }

        if (!_adapter.TryGetValue(impostazioni.Provider, out var adapter))
        {
            throw new ConfigurazioneEmailNonValidaException($"Il canale {impostazioni.Provider} non è ancora disponibile.");
        }

        await adapter.InviaAsync(impostazioni, messaggio, annullamento);
    }

    private ImpostazioniEmail Leggi(ImpostazioniEmailTenant riga)
    {
        var valori = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var coppia in JsonSerializer.Deserialize<Dictionary<string, string>>(riga.ValoriJson) ?? [])
            {
                valori[coppia.Key] = coppia.Value;
            }

            if (!string.IsNullOrEmpty(riga.SegretiProtetti))
            {
                foreach (var coppia in JsonSerializer.Deserialize<Dictionary<string, string>>(_protettore.Rivela(riga.SegretiProtetti)) ?? [])
                {
                    valori[coppia.Key] = coppia.Value;
                }
            }
        }
        catch (JsonException eccezione)
        {
            throw new ConfigurazioneEmailNonValidaException("Impostazioni email illeggibili.", eccezione);
        }
        catch (InvalidOperationException eccezione)
        {
            throw new ConfigurazioneEmailNonValidaException("Impostazioni email illeggibili: chiave dei segreti non valida o mancante.", eccezione);
        }

        return new ImpostazioniEmail(riga.Provider, valori);
    }
}

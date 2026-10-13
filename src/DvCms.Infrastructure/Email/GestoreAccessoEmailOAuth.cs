using DvCms.Contracts.Email;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.Infrastructure.Persistenza;
using DvCms.Models.Email;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Email;

/// <summary>Il token di accesso da usare per un invio e la casella (mittente) a cui appartiene.</summary>
public sealed record AccessoEmailOAuth(string Token, string Casella)
{
    public override string ToString() => $"AccessoEmailOAuth({Casella})";
}

/// <summary>
/// Dà al worker il token di accesso per inviare dalla casella collegata (ADR-0021): riusa quello salvato finché è valido, altrimenti
/// lo rinnova con il refresh token e salva il nuovo. Se il provider risponde <c>invalid_grant</c> segna il collegamento come revocato:
/// gli invii successivi falliscono subito, senza chiamare il provider, finché il <c>TenantAdmin</c> non ricollega la casella.
/// </summary>
public sealed class GestoreAccessoEmailOAuth
{
    private static readonly TimeSpan _margine = TimeSpan.FromMinutes(2);

    private readonly ContestoDatabase _contesto;
    private readonly IProtettoreSegreti _protettore;
    private readonly ClienteOAuthEmail _cliente;
    private readonly TimeProvider _orologio;

    public GestoreAccessoEmailOAuth(ContestoDatabase contesto, IProtettoreSegreti protettore, ClienteOAuthEmail cliente, TimeProvider orologio)
    {
        _contesto = contesto;
        _protettore = protettore;
        _cliente = cliente;
        _orologio = orologio;
    }

    /// <exception cref="ConfigurazioneEmailNonValidaException">La casella non è collegata, il collegamento è revocato o non corrisponde all'app.</exception>
    /// <exception cref="InvioEmailFallitoException">Il provider non è raggiungibile o ha risposto in modo inatteso (si ritenta).</exception>
    public async Task<AccessoEmailOAuth> OttieniAsync(Guid idTenant, ImpostazioniEmail impostazioni, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);

        var connessione = await _contesto.ConnessioniEmailOAuth.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.IdTenant == idTenant, annullamento)
            ?? throw new ConfigurazioneEmailNonValidaException("La casella del tenant non è collegata: il TenantAdmin deve completare il collegamento.");
        if (connessione.Provider != impostazioni.Provider || connessione.ImprontaApp != ClienteOAuthEmail.Impronta(impostazioni.Provider, impostazioni.Valori))
        {
            throw new ConfigurazioneEmailNonValidaException("Il collegamento non corrisponde all'app OAuth configurata: ricollegare la casella.");
        }

        if (connessione.Stato == StatoConnessioneEmail.Revocata)
        {
            throw new ConfigurazioneEmailNonValidaException("Il consenso della casella è stato revocato: ricollegarla.");
        }

        var adesso = _orologio.GetUtcNow().UtcDateTime;
        try
        {
            if (connessione.TokenAccessoProtetto is { Length: > 0 } salvato && connessione.ScadenzaAccessoUtc is { } scadenza && scadenza - adesso > _margine)
            {
                return new AccessoEmailOAuth(_protettore.Rivela(salvato), connessione.Casella);
            }
        }
        catch (InvalidOperationException)
        {
            // Chiave dei segreti cambiata: il token salvato non si legge, si rinnova (se anche il refresh token è illeggibile lo dice sotto).
        }

        TokenOAuth rinnovato;
        try
        {
            rinnovato = await _cliente.RinnovaAsync(impostazioni.Provider, impostazioni.Valori, _protettore.Rivela(connessione.TokenAggiornamentoProtetto), annullamento);
        }
        catch (InvalidOperationException eccezione)
        {
            throw new ConfigurazioneEmailNonValidaException("Collegamento illeggibile: chiave dei segreti non valida o mancante. Ricollegare la casella.", eccezione);
        }
        catch (ConsensoRevocatoException)
        {
            connessione.Stato = StatoConnessioneEmail.Revocata;
            connessione.UltimoErrore = "invalid_grant";
            connessione.UltimoErroreIlUtc = adesso;
            connessione.TokenAccessoProtetto = null;
            connessione.ScadenzaAccessoUtc = null;
            await SalvaSeNonInConflittoAsync(annullamento);
            throw new ConfigurazioneEmailNonValidaException("Il consenso della casella è stato revocato dal provider: ricollegarla.");
        }
        catch (InvioEmailFallitoException eccezione)
        {
            connessione.UltimoErrore = eccezione.Message.Length > 200 ? eccezione.Message[..200] : eccezione.Message;
            connessione.UltimoErroreIlUtc = adesso;
            await SalvaSeNonInConflittoAsync(annullamento);
            throw;
        }

        connessione.TokenAccessoProtetto = _protettore.Proteggi(rinnovato.Accesso);
        connessione.ScadenzaAccessoUtc = rinnovato.ScadenzaUtc;
        if (rinnovato.Aggiornamento is not null)
        {
            // Microsoft ruota il refresh token a ogni rinnovo.
            connessione.TokenAggiornamentoProtetto = _protettore.Proteggi(rinnovato.Aggiornamento);
        }

        connessione.UltimoRinnovoIlUtc = adesso;
        connessione.UltimoErrore = null;
        connessione.UltimoErroreIlUtc = null;
        await SalvaSeNonInConflittoAsync(annullamento);
        return new AccessoEmailOAuth(rinnovato.Accesso, connessione.Casella);
    }

    /// <summary>Se un altro invio ha aggiornato la riga nel frattempo, il suo valore è altrettanto valido: non si ritenta.</summary>
    private async Task SalvaSeNonInConflittoAsync(CancellationToken annullamento)
    {
        try
        {
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Rinnovo concorrente: va bene il risultato dell'altro.
        }
    }
}

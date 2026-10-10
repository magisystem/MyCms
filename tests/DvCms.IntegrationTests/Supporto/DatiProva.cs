using DvCms.Application.Accessi;
using DvCms.Domain.Accessi;
using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>Dati di prova per i test di integrazione.</summary>
public static class DatiProva
{
    public sealed record TenantCreato(Guid IdTenant, Guid IdSito, Guid IdDominio, string Host);

    public static async Task<TenantCreato> CreaTenantAsync(
        IServiceProvider fornitore,
        string nome,
        string host,
        bool primario = false,
        bool soloSviluppo = false,
        bool verificato = true,
        bool dominioAttivo = true,
        bool abilitaInglese = true,
        StatoTenant statoTenant = StatoTenant.Attivo,
        StatoSito statoSito = StatoSito.Attivo,
        CancellationToken annullamento = default)
    {
        var orologio = TimeProvider.System;
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        var tenant = Tenant.Crea(nome, primario, orologio);
        tenant.Stato = statoTenant;
        var sito = Sito.Crea(tenant.Id, $"Sito di {nome}", null, orologio);
        sito.Stato = statoSito;
        var dominio = DominioSito.CreaVerificato(tenant.Id, sito.Id, host, soloSviluppo, null, orologio);
        dominio.Attivo = dominioAttivo;
        if (!verificato)
        {
            dominio.VerificatoIlUtc = null;
        }

        contesto.Tenant.Add(tenant);
        contesto.Siti.Add(sito);
        contesto.DominiSito.Add(dominio);
        contesto.LingueSito.Add(LinguaSito.Crea(tenant.Id, sito.Id, DvCms.Domain.Localizzazione.Lingua.IdItaliano, predefinita: true, orologio));
        if (abilitaInglese)
        {
            contesto.LingueSito.Add(LinguaSito.Crea(tenant.Id, sito.Id, DvCms.Domain.Localizzazione.Lingua.IdInglese, predefinita: false, orologio));
        }

        contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, orologio));
        await contesto.SaveChangesAsync(annullamento);

        return new TenantCreato(tenant.Id, sito.Id, dominio.Id, dominio.Host);
    }

    /// <summary>Configura per il tenant l'invio SMTP verso un server finto locale (senza autenticazione né TLS).</summary>
    public static async Task ConfiguraSmtpAsync(IServiceProvider fornitore, Guid idTenant, int porta, CancellationToken annullamento = default)
    {
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        contesto.ImpostazioniEmailTenant.Add(new DvCms.Domain.Email.ImpostazioniEmailTenant
        {
            Id = DvCms.Domain.Identificativi.Nuovo(TimeProvider.System),
            IdTenant = idTenant,
            Provider = DvCms.Domain.Email.ProviderEmail.Smtp,
            ValoriJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["Host"] = "127.0.0.1",
                ["Porta"] = porta.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ModalitaTls"] = "Nessuna",
                ["IndirizzoMittente"] = "noreply@esempio.it",
            }),
            AggiornatoIlUtc = DateTime.UtcNow,
        });
        await contesto.SaveChangesAsync(annullamento);
    }

    public const string PasswordProva = "password-di-prova-123";

    /// <summary>Crea un account con password nota (<see cref="PasswordProva"/> se non indicata).</summary>
    public static async Task<Guid> CreaUtenzaAsync(
        IServiceProvider fornitore,
        RuoloUtenza ruolo,
        Guid? idTenant,
        string email,
        string? password = null,
        bool deveCambiarePassword = false,
        StatoUtenza stato = StatoUtenza.Attiva,
        CancellationToken annullamento = default)
    {
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var hash = ambito.ServiceProvider.GetRequiredService<IHashPassword>();
        var orologio = ambito.ServiceProvider.GetRequiredService<TimeProvider>();

        var utenza = Utenza.Crea(ruolo, idTenant, email, $"Utente {email}", hash.Calcola(password ?? PasswordProva), deveCambiarePassword, orologio);
        utenza.Stato = stato;
        contesto.Utenze.Add(utenza);
        await contesto.SaveChangesAsync(annullamento);
        return utenza.Id;
    }

    /// <summary>
    /// Attiva la 2FA di un account direttamente sul database (come dopo una configurazione completata) e restituisce il segreto
    /// e i codici di recupero in chiaro. Richiede la chiave dei segreti nella configurazione.
    /// </summary>
    public static async Task<(byte[] Segreto, IReadOnlyList<string> CodiciRecupero)> AttivaDueFattoriAsync(
        IServiceProvider fornitore,
        Guid idUtenza,
        CancellationToken annullamento = default)
    {
        var segreto = DvCms.Application.Accessi.DueFattori.Totp.GeneraSegreto();
        var codici = DvCms.Application.Accessi.DueFattori.CodiciRecupero.Genera();
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var protettore = ambito.ServiceProvider.GetRequiredService<DvCms.Application.Email.IProtettoreSegreti>();
        var protetto = protettore.Proteggi(DvCms.Application.Accessi.DueFattori.Base32.Codifica(segreto));
        var adesso = DateTime.UtcNow;
        await contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza).ExecuteUpdateAsync(
            u => u.SetProperty(x => x.SegretoTotpProtetto, protetto)
                .SetProperty(x => x.DueFattoriAttivi, true)
                .SetProperty(x => x.DueFattoriAttivatiIlUtc, adesso)
                .SetProperty(x => x.UltimoPassoTotp, 0L),
            annullamento);
        foreach (var codice in codici)
        {
            contesto.CodiciRecuperoUtenza.Add(new CodiceRecuperoUtenza
            {
                Id = Guid.NewGuid(),
                IdUtenza = idUtenza,
                HashCodice = DvCms.Application.Accessi.DueFattori.CodiciRecupero.Hash(DvCms.Application.Accessi.DueFattori.CodiciRecupero.Normalizza(codice)!),
                CreatoIlUtc = adesso,
            });
        }

        await contesto.SaveChangesAsync(annullamento);
        return (segreto, codici);
    }
}

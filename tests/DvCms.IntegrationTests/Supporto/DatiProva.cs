using DvCms.Application.Accessi;
using DvCms.Domain.Accessi;
using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Persistenza;
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
}

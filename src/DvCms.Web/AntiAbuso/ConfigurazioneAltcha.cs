using System.Globalization;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Altcha;
using Altcha.AspNetCore;
using DvCms.Web.Accessi;
using Microsoft.Extensions.Options;

namespace DvCms.Web.AntiAbuso;

/// <summary>
/// Configurazione di ALTCHA (ADR-0026): prova di lavoro sulle form pubbliche. La chiave di firma è una sola per l'installazione
/// (<c>Altcha:HmacSignatureSecret</c>, dai segreti o dall'ambiente, mai nel repository). In sviluppo, senza chiave, se ne genera una
/// a ogni avvio; altrove la sua assenza è un errore di configurazione segnalato all'avvio e le form non accettano nulla.
/// </summary>
public static partial class ConfigurazioneAltcha
{
    public const string ChiaveAbilitato = "Altcha:Abilitato";
    public const string ChiaveSegreto = "Altcha:HmacSignatureSecret";
    public const string ChiaveRichiesteAlMinuto = "Altcha:RichiesteSfidaAlMinuto";
    public const string PercorsoSfida = "/altcha/sfida";
    public const string PoliticaLimiteSfida = "sfida-altcha";
    public const int RichiesteSfidaAlMinutoPredefinite = 30;

    /// <summary>
    /// Difficoltà predefinita della sfida, più leggera di quella della libreria (5000 iterazioni, contatore fra 5000 e 10000): il lavoro
    /// medio del visitatore è circa un quarto, qualche secondo anche su un telefono. La protezione vera sono la firma, la scadenza,
    /// l'uso unico e il limite di richieste; il lavoro è un freno in più. Si cambia con <c>Altcha:Challenge:Cost</c>,
    /// <c>CounterMin</c> e <c>CounterMax</c>.
    /// </summary>
    public const int CostoPredefinito = 2500;

    public const int ContatoreMinimoPredefinito = 2000;
    public const int ContatoreMassimoPredefinito = 5000;

    /// <summary>Vero se la protezione è accesa (predefinito: sì).</summary>
    public static bool Abilitato(IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(configurazione);

        var testo = configurazione[ChiaveAbilitato];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return true;
        }

        return bool.TryParse(testo, out var valore)
            ? valore
            : throw new InvalidOperationException($"Valore non valido per '{ChiaveAbilitato}': atteso true o false.");
    }

    /// <summary>Registra ALTCHA e la verifica usata dalle pagine. Con la protezione spenta registra solo la verifica che accetta tutto.</summary>
    public static IServiceCollection AggiungiAltcha(this IServiceCollection servizi, IConfiguration configurazione, IHostEnvironment ambiente)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);
        ArgumentNullException.ThrowIfNull(ambiente);

        if (!Abilitato(configurazione))
        {
            servizi.AddSingleton<IVerificaAntiAbuso, VerificaDisabilitata>();
            return servizi;
        }

        var segreto = configurazione[ChiaveSegreto];
        if (string.IsNullOrWhiteSpace(segreto) && ambiente.IsDevelopment())
        {
            segreto = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        servizi.AddAltcha(configurazione.GetSection(AltchaDefaults.ConfigurationSection));
        servizi.PostConfigure<AltchaOptions>(opzioni =>
        {
            if (string.IsNullOrWhiteSpace(configurazione[$"{AltchaDefaults.ConfigurationSection}:Challenge:Cost"]))
            {
                opzioni.Challenge.Cost = CostoPredefinito;
            }

            if (string.IsNullOrWhiteSpace(configurazione[$"{AltchaDefaults.ConfigurationSection}:Challenge:CounterMin"]) && string.IsNullOrWhiteSpace(configurazione[$"{AltchaDefaults.ConfigurationSection}:Challenge:CounterMax"]))
            {
                opzioni.Challenge.CounterMin = ContatoreMinimoPredefinito;
                opzioni.Challenge.CounterMax = ContatoreMassimoPredefinito;
            }

            if (!string.IsNullOrWhiteSpace(segreto))
            {
                opzioni.HmacSignatureSecret = segreto;
                opzioni.HmacKeySignatureSecret = AltchaSecrets.DeriveHmacKeySecret(segreto);
            }
        });
        servizi.AddScoped<IVerificaAntiAbuso, VerificaAltcha>();

        var richieste = LeggiRichiesteAlMinuto(configurazione);
        servizi.AddRateLimiter(opzioni => opzioni.AddPolicy(PoliticaLimiteSfida, contesto =>
            RateLimitPartition.GetFixedWindowLimiter(
                ChiaveIndirizzoIp.Calcola(contesto.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = richieste, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return servizi;
    }

    /// <summary>Avviso all'avvio se la protezione è accesa ma manca la chiave (fuori dallo sviluppo).</summary>
    public static void AvvisaSeMancaLaChiave(IConfiguration configurazione, IHostEnvironment ambiente, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configurazione);
        ArgumentNullException.ThrowIfNull(ambiente);
        ArgumentNullException.ThrowIfNull(logger);

        if (Abilitato(configurazione) && !ambiente.IsDevelopment() && string.IsNullOrWhiteSpace(configurazione[ChiaveSegreto]))
        {
            ChiaveMancante(logger);
        }
    }

    /// <summary>Espone la sfida, con risposta mai conservata in cache e limite di richieste per indirizzo. Nulla se la protezione è spenta.</summary>
    public static IEndpointRouteBuilder MapSfidaAltcha(this IEndpointRouteBuilder rotte, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(rotte);
        ArgumentNullException.ThrowIfNull(configurazione);

        if (Abilitato(configurazione))
        {
            rotte.MapAltchaChallenge(PercorsoSfida)
                .RequireRateLimiting(PoliticaLimiteSfida)
                .AddEndpointFilter(async (contesto, successivo) =>
                {
                    contesto.HttpContext.Response.Headers.CacheControl = "no-store";
                    return await successivo(contesto);
                });
        }

        return rotte;
    }

    private static int LeggiRichiesteAlMinuto(IConfiguration configurazione)
    {
        var testo = configurazione[ChiaveRichiesteAlMinuto];
        if (string.IsNullOrWhiteSpace(testo))
        {
            return RichiesteSfidaAlMinutoPredefinite;
        }

        return int.TryParse(testo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valore) && valore >= 1
            ? valore
            : throw new InvalidOperationException($"Valore non valido per '{ChiaveRichiesteAlMinuto}': atteso un intero positivo.");
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Altcha è attivo ma Altcha:HmacSignatureSecret non è impostato: le form pubbliche (registrazione, recupero password) rifiutano ogni richiesta. Imposta la chiave (segreti utente, variabile Altcha__HmacSignatureSecret o appsettings.Local.json) oppure spegni la protezione con Altcha:Abilitato=false")]
    private static partial void ChiaveMancante(ILogger logger);
}

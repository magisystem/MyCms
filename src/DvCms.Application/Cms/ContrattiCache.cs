namespace DvCms.Application.Cms;

/// <summary>
/// Registra che i contenuti pubblici di un sito sono cambiati (G-002). Come <c>IOutbox</c>, non salva: la riga entra nella
/// stessa <c>SaveChanges</c> della modifica, quindi o ci sono entrambe o nessuna. Dopo il salvataggio l'istanza che ha
/// scritto svuota subito la propria cache; le altre istanze lo fanno alla lettura successiva del registro.
/// </summary>
public interface IInvalidazioneCache
{
    void Registra(Guid idTenant, Guid idSito);
}

/// <summary>
/// Cache in memoria dei contenuti pubblici, separata per sito (G-002). Ogni voce ha una durata massima (rete di sicurezza per
/// modifiche che non passano dall'applicazione) e dipende dalla "generazione" del sito: invalidare il sito incrementa la
/// generazione e rende subito irraggiungibili tutte le sue voci. Non memorizza mai assenze (404) né contenuti personali o di anteprima.
/// </summary>
public interface ICachePubblica
{
    /// <summary>La voce in cache per sito e chiave, oppure il risultato di <paramref name="carica"/> (memorizzato se non nullo).</summary>
    Task<T?> OttieniAsync<T>(Guid idSito, string chiave, Func<Task<T?>> carica)
        where T : class;

    /// <summary>Rende non più valide tutte le voci del sito.</summary>
    void Invalida(Guid idSito);
}

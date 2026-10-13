namespace DvCms.Models.Accessi;

/// <summary>
/// Ruoli degli account (ADR-0011). <c>Guest</c> non è un account: è il visitatore anonimo di un tenant.
/// </summary>
public enum RuoloUtenza
{
    /// <summary>Operatore della piattaforma: globale, senza tenant.</summary>
    PlatformAdmin = 1,

    /// <summary>Amministratore di un solo tenant (un tenant può averne più di uno).</summary>
    TenantAdmin = 2,

    /// <summary>Acquirente registrato di un sito con e-commerce (da M7).</summary>
    Utente = 3,
}

public enum StatoUtenza
{
    Attiva = 1,
    Disabilitata = 2,

    /// <summary>Acquirente registrato che non ha ancora confermato l'email e scelto la password: non può accedere.</summary>
    InAttesaDiConferma = 3,

    /// <summary>Acquirente il cui account è stato anonimizzato su richiesta (A7): la riga resta solo perché gli ordini la referenziano; non può più accedere.</summary>
    Anonimizzata = 4,
}

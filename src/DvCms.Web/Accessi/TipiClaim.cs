namespace DvCms.Web.Accessi;

/// <summary>Tipi di claim e nomi di schemi e politiche dell'autenticazione web.</summary>
public static class TipiClaim
{
    /// <summary>Tenant dell'host su cui è stata creata la sessione.</summary>
    public const string IdTenant = "dvcms:id-tenant";

    public const string TimbroSicurezza = "dvcms:timbro-sicurezza";

    public const string DeveCambiarePassword = "dvcms:deve-cambiare-password";

    public const string CodiceLingua = "dvcms:codice-lingua";

    /// <summary>La 2FA è obbligatoria per il ruolo ma l'account non l'ha ancora configurata.</summary>
    public const string DeveAttivareDueFattori = "dvcms:deve-attivare-2fa";
}

public static class Schemi
{
    /// <summary>Schema di autenticazione (e nome logico del cookie) dell'area di amministrazione.</summary>
    public const string Admin = "DvCmsAdmin";
}

public static class Politiche
{
    /// <summary><c>TenantAdmin</c> del tenant dell'host oppure <c>PlatformAdmin</c> (che lavora come un <c>TenantAdmin</c>).</summary>
    public const string AmministratoreTenant = "AmministratoreTenant";

    public const string AmministratorePiattaforma = "AmministratorePiattaforma";

    public const string LimiteAccesso = "accesso";
}

public static class RuoliGuest
{
    public const string Guest = "Guest";
}

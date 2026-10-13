using DvCms.Extender;

namespace DvCms.Models.Cms;

/// <summary>
/// Reindirizzamento permanente (301) da un vecchio slug alla pagina che lo ha lasciato. Punta alla pagina e non a un
/// indirizzo: la destinazione è sempre lo slug attuale (un solo salto, nessuna catena), e se la pagina cambia ancora
/// slug il reindirizzamento la segue. Se un altro slug identico torna in uso, il reindirizzamento viene tolto.
/// </summary>
public class RedirectPagina
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdLingua { get; set; }

    public string SlugOrigine { get; set; } = string.Empty;

    public Guid IdPagina { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public static RedirectPagina Crea(Pagina pagina, string slugOrigine, TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        ArgumentNullException.ThrowIfNull(orologio);
        if (!NormalizzatoreSlug.TentaNormalizza(slugOrigine, out var slug))
        {
            throw new ArgumentException("Slug di origine non valido.", nameof(slugOrigine));
        }

        if (string.Equals(slug, pagina.Slug, StringComparison.Ordinal))
        {
            throw new ArgumentException("Un reindirizzamento non può puntare allo stesso slug (sarebbe un ciclo).", nameof(slugOrigine));
        }

        return new RedirectPagina
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = pagina.IdTenant,
            IdSito = pagina.IdSito,
            IdLingua = pagina.IdLingua,
            SlugOrigine = slug,
            IdPagina = pagina.Id,
            CreatoIlUtc = orologio.GetUtcNow().UtcDateTime,
        };
    }
}

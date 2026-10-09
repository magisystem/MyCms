namespace DvCms.Domain.Cms;

public enum StatoPagina
{
    /// <summary>Mai pubblicata, oppure ritirata: non è visibile al pubblico.</summary>
    Bozza = 1,

    /// <summary>Ha una revisione pubblicata (<see cref="Pagina.IdRevisionePubblicata"/>).</summary>
    Pubblicata = 2,
}

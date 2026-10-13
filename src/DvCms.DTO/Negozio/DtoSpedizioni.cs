using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

public sealed record DatiMetodoSpedizione(Guid? Id, TipoMetodoSpedizione Tipo, AreaSpedizione? Area, string? Nome, decimal TariffaLorda, bool Attivo);

public sealed record MetodoSpedizioneVista(Guid Id, TipoMetodoSpedizione Tipo, AreaSpedizione? Area, string Nome, decimal TariffaLorda, bool Attivo);

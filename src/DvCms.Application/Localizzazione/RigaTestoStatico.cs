namespace DvCms.Application.Localizzazione;

/// <summary>Un testo statico in una lingua, come conservato nell'archivio.</summary>
public sealed record RigaTestoStatico(string CodiceLingua, string Chiave, string Testo, bool DaTradurre);

/// <summary>Lingua con la sua cultura, per formattare i testi.</summary>
public sealed record InfoLingua(string Codice, string Cultura, bool Predefinita, bool Attiva);

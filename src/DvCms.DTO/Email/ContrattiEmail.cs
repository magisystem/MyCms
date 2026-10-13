using DvCms.Models.Email;

namespace DvCms.DTO.Email;

/// <summary>Messaggio da inviare. Il mittente non è qui: lo decide la configurazione del tenant.</summary>
public sealed record MessaggioEmail(string Destinatario, string? NomeDestinatario, string Oggetto, string CorpoTesto, string? CorpoHtml = null);

/// <summary>Impostazioni complete di un tenant, segreti compresi, pronte per un adapter. Non va mai registrata nei log.</summary>
public sealed record ImpostazioniEmail(ProviderEmail Provider, IReadOnlyDictionary<string, string> Valori)
{
    public string? Valore(string nome) => Valori.TryGetValue(nome, out var valore) && !string.IsNullOrWhiteSpace(valore) ? valore : null;

    public override string ToString() => $"ImpostazioniEmail({Provider}, {Valori.Count} valori)";
}

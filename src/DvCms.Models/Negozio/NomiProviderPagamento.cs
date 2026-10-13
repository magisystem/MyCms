
namespace DvCms.Models.Negozio;

/// <summary>I provider di pagamento che il negozio sa usare (ADR-0020, ADR-0022). Il nome è anche quello salvato nei tentativi di pagamento.</summary>
public static class NomiProviderPagamento
{
    public const string PayPal = "PayPal";
    public const string Nexi = "Nexi";

    public static IReadOnlyList<string> Tutti { get; } = [PayPal, Nexi];
}

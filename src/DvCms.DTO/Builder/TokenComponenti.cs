namespace DvCms.DTO.Builder;

/// <summary>Un token di design dei componenti del builder: una proprietà CSS personalizzata (<c>--dv-*</c>) che un tema può impostare.</summary>
/// <param name="Nome">Nome completo, ad esempio <c>--dv-colore-primario</c>.</param>
/// <param name="Descrizione">A cosa serve (finisce nella documentazione).</param>
/// <param name="Predefinito">Valore che i componenti usano quando il tema non lo imposta; è il valore di riserva di ogni <c>var()</c> in <c>componenti.css</c>.</param>
public sealed record TokenComponente(string Nome, string Descrizione, string Predefinito);

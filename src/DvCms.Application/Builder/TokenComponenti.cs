namespace DvCms.Application.Builder;

/// <summary>Un token di design dei componenti del builder: una proprietà CSS personalizzata (<c>--dv-*</c>) che un tema può impostare.</summary>
/// <param name="Nome">Nome completo, ad esempio <c>--dv-colore-primario</c>.</param>
/// <param name="Descrizione">A cosa serve (finisce nella documentazione).</param>
/// <param name="Predefinito">Valore che i componenti usano quando il tema non lo imposta; è il valore di riserva di ogni <c>var()</c> in <c>componenti.css</c>.</param>
public sealed record TokenComponente(string Nome, string Descrizione, string Predefinito);

/// <summary>
/// Contratto fra componenti del builder e temi (M5). I componenti si disegnano con markup indipendente dal tema e con uno stile che
/// legge **solo** i token elencati qui (con il valore predefinito come riserva in ogni <c>var()</c>); un tema che vuole ritoccare i
/// componenti fornisce una "skin" (un foglio che imposta soltanto questi token). Aggiungere un token significa aggiornare questo elenco,
/// <c>componenti.css</c> e la documentazione: un test controlla che i tre restino allineati.
/// </summary>
public static class TokenComponenti
{
    public const string Prefisso = "--dv-";

    public static IReadOnlyList<TokenComponente> Tutti { get; } =
    [
        new("--dv-larghezza-contenuto", "Larghezza massima del contenuto di una sezione.", "46rem"),
        new("--dv-spazio-compatta", "Spazio verticale di una sezione con spaziatura compatta.", "1rem"),
        new("--dv-spazio-normale", "Spazio verticale di una sezione con spaziatura normale.", "2rem"),
        new("--dv-spazio-ampia", "Spazio verticale di una sezione con spaziatura ampia.", "4rem"),
        new("--dv-colore-primario", "Colore di accento: sfondo \"primario\", pulsanti, bordo del pulsante secondario, evidenziazione del focus.", "var(--tema-colore-primario, #1f4e8c)"),
        new("--dv-colore-sfondo-scuro", "Sfondo delle sezioni, degli hero e degli inviti all'azione \"scuri\".", "var(--tema-colore-intestazione, #111827)"),
        new("--dv-colore-testo-su-scuro", "Colore del testo sugli sfondi scuri e primari.", "#ffffff"),
        new("--dv-colore-sfondo-chiaro", "Sfondo delle sezioni \"chiare\".", "#f1f5f9"),
        new("--dv-colore-testo-su-chiaro", "Colore del testo sugli sfondi chiari.", "#1f2933"),
        new("--dv-colore-sfondo-chiaro-notte", "Sfondo delle sezioni \"chiare\" con preferenza per la modalità scura del sistema.", "#1f2933"),
        new("--dv-colore-testo-su-chiaro-notte", "Colore del testo sugli sfondi chiari con preferenza per la modalità scura.", "#e4e7eb"),
        new("--dv-colore-titoli", "Colore dei titoli fuori dagli sfondi scuri.", "inherit"),
        new("--dv-colore-bordo", "Colore dei filetti (fisarmonica).", "#cbd2d9"),
        new("--dv-colore-didascalia", "Colore delle didascalie delle immagini.", "#52606d"),
        new("--dv-raggio-pulsante", "Raggio degli angoli dei pulsanti.", "0.4rem"),
        new("--dv-durata-animazione", "Durata delle animazioni di comparsa.", "0.6s"),
    ];

    public static bool Esiste(string nome) => Tutti.Any(t => string.Equals(t.Nome, nome, StringComparison.Ordinal));
}

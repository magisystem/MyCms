namespace DvCms.Contracts.Accessi;

/// <summary>Hash delle password (ADR-0016: BCrypt). L'hash contiene già algoritmo, costo e sale.</summary>
public interface IHashPassword
{
    string Calcola(string password);

    /// <summary>Verifica a tempo costante; un hash non valido restituisce falso.</summary>
    bool Verifica(string password, string hash);

    /// <summary>Vero se l'hash è stato calcolato con un costo inferiore a quello configurato.</summary>
    bool RichiedeAggiornamento(string hash);
}

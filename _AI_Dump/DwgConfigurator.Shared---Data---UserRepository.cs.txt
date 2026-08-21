using System.Data.SQLite;
using DwgConfigurator.Shared.Config;

namespace DwgConfigurator.Shared.Data;

/// <summary>
/// Legge check_user e approved_user dalla vista v_user_check_approved in UserDB.db.
/// Il matching e' robusto (case-insensitive, swap nome/cognome).
/// </summary>
public class UserRepository
{
    public (string CheckUser, string ApprovedUser) GetUserInfo(string windowsUser)
    {
        string checkUser = string.Empty;
        string approvedUser = string.Empty;

        if (string.IsNullOrWhiteSpace(windowsUser))
            return (checkUser, approvedUser);

        var dbPath = AppSettings.UserDbPath;
        if (string.IsNullOrEmpty(dbPath) || !System.IO.File.Exists(dbPath))
            return (checkUser, approvedUser);

        try
        {
            var connStr = $"Data Source={dbPath};Version=3;Read Only=True;";
            using var conn = new SQLiteConnection(connStr);
            conn.Open();

            using var cmd = new SQLiteCommand(
                "SELECT user_name, check_user, approved_user FROM v_user_check_approved", conn);
            using var reader = cmd.ExecuteReader();

            var inputTokens = TokenizeName(windowsUser);
            if (inputTokens.Length == 0) return (checkUser, approvedUser);

            string inputNorm = string.Join(" ", inputTokens);
            string inputSwap = SwapFirstLast(inputTokens);

            while (reader.Read())
            {
                string dbUser = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var dbTokens = TokenizeName(dbUser);
                if (dbTokens.Length == 0) continue;

                string dbNorm = string.Join(" ", dbTokens);
                string dbSwap = SwapFirstLast(dbTokens);

                if (Eq(dbNorm, inputNorm) || Eq(dbNorm, inputSwap) ||
                    Eq(dbSwap, inputNorm) || Eq(dbSwap, inputSwap))
                {
                    checkUser = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    approvedUser = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    break;
                }
            }
        }
        catch { /* DB non raggiungibile: campi restano vuoti, l'utente li compila a mano */ }

        return (checkUser, approvedUser);
    }

    private static string[] TokenizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
        s = s.ToUpperInvariant().Trim();
        var matches = System.Text.RegularExpressions.Regex.Matches(s, @"[A-Z0-9]+");
        var tokens = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            tokens[i] = matches[i].Value;
        return tokens;
    }

    private static string SwapFirstLast(string[] tokens)
    {
        if (tokens.Length < 2) return string.Join(" ", tokens);
        return tokens[^1] + " " + string.Join(" ", tokens, 0, tokens.Length - 1);
    }

    private static bool Eq(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

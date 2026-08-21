using System.Data;
using System.Data.SQLite;
using DwgConfigurator.Shared.Config;

namespace DwgConfigurator.Shared.Data;

public static class DbConnectionFactory
{
    public static IDbConnection Create()
    {
        var connection = new SQLiteConnection(AppSettings.ConnectionString);
        connection.Open();
        using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
            cmd.ExecuteNonQuery();
        return connection;
    }
}

using System.IO;

namespace DwgConfigurator.Shared.Config;

public static class AppSettings
{
    public static string DbPath { get; set; }
    public static string TemplateFolderPath { get; set; }
    public static string OutputFolderPath { get; set; }

    /// <summary>Percorso del database utenti (UserDB.db) per check/approved.</summary>
    public static string UserDbPath { get; set; }

    /// <summary>Connection string verso il database SAP (SQL Server).</summary>
    public static string SapConnectionString { get; set; }

    static AppSettings()
    {
        var root = ResolveAppRoot();
        DbPath             = Path.Combine(root, "DwgConfigurator.db");
        TemplateFolderPath = Path.Combine(root, "Templates");
        OutputFolderPath   = Path.Combine(root, "Output");
        UserDbPath         = Path.Combine(root, "UserDB.db");
        Directory.CreateDirectory(TemplateFolderPath);
        Directory.CreateDirectory(OutputFolderPath);

        SapConnectionString = @"Server=SRV-RED-ING\SQL2017;Database=AB_Components;Integrated Security=True;Connection Timeout=5;";
    }

    public static string ConnectionString =>
        $"Data Source={DbPath};Version=3;";

    private static string ResolveAppRoot()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return baseDir;
    }
}

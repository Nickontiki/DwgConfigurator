using Dapper;

namespace DwgConfigurator.Shared.Data;

public static class DatabaseInitializer
{
    public static void Initialize()
    {
        using var db = DbConnectionFactory.Create();

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS ProductGroups (
                Id            INTEGER PRIMARY KEY,
                Name          TEXT    NOT NULL,
                Description   TEXT    DEFAULT '',
                CartiglioPath TEXT    DEFAULT ''
            );
        ");

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS Modules (
                Id    INTEGER PRIMARY KEY,
                Name  TEXT NOT NULL,
                Sigla TEXT NOT NULL
            )");

        db.Execute(@"INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (1, 'Modulo motore',    'MM')");
        db.Execute(@"INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (2, 'Modulo biochange', 'MB')");
        db.Execute(@"INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (3, 'Modulo ausiliari', 'MA')");
        db.Execute(@"INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (4, 'Skid biogas',      'SB')");
        db.Execute(@"INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (5, 'Compressore',      'CG1')");

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS ProductTypes (
                Id              INTEGER PRIMARY KEY,
                ProductGroupId  INTEGER NOT NULL,
                ModuleId        INTEGER NULL,
                Prodotto        TEXT    NOT NULL DEFAULT '',
                Taglia          TEXT    DEFAULT '',
                Famiglia        TEXT    DEFAULT '',
                Carpenteria     TEXT    DEFAULT '',
                Temperatura     TEXT    NOT NULL DEFAULT 'Standard' CHECK(Temperatura IN ('Standard','-20°C')),
                FOREIGN KEY (ProductGroupId) REFERENCES ProductGroups(Id) ON DELETE CASCADE,
                FOREIGN KEY (ModuleId) REFERENCES Modules(Id) ON DELETE SET NULL
            );
        ");

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS DwgTemplates (
                Id              INTEGER PRIMARY KEY,
                ProductTypeId   INTEGER NOT NULL,
                TemplatePath    TEXT    NOT NULL,
                TemplateType    TEXT    NOT NULL CHECK(TemplateType IN ('Cartiglio','Layout')),
                Format          TEXT    DEFAULT '',
                Scale           TEXT    DEFAULT '',
                FOREIGN KEY (ProductTypeId) REFERENCES ProductTypes(Id) ON DELETE CASCADE
            );
        ");

        db.Execute(@"
            CREATE TABLE IF NOT EXISTS FixedAttributes (
                Id              INTEGER PRIMARY KEY,
                ProductTypeId   INTEGER NOT NULL,
                AttributeTag    TEXT    NOT NULL,
                FixedValue      TEXT    DEFAULT '',
                AppliesTo       TEXT    NOT NULL DEFAULT 'Cartiglio' CHECK(AppliesTo IN ('Cartiglio','Layout')),
                FOREIGN KEY (ProductTypeId) REFERENCES ProductTypes(Id) ON DELETE CASCADE
            );
        ");

        AddColumnIfMissing("ProductGroups", "CartiglioPath", "TEXT DEFAULT ''");
        AddColumnIfMissing("ProductTypes", "ModuleId", "INTEGER NULL");
        AddColumnIfMissing("ProductTypes", "Famiglia", "TEXT DEFAULT ''");
        AddColumnIfMissing("ProductTypes", "Temperatura", "TEXT NOT NULL DEFAULT 'Standard'");

        db.Execute(@"UPDATE ProductTypes SET Famiglia = COALESCE(NULLIF(Famiglia, ''), Prodotto) WHERE Famiglia IS NULL OR Famiglia = ''");
        db.Execute(@"UPDATE ProductTypes SET Temperatura = CASE WHEN Taglia = '-20°C' THEN '-20°C' ELSE 'Standard' END WHERE Temperatura IS NULL OR Temperatura = ''");

        db.Execute(@"
            UPDATE ProductGroups SET CartiglioPath = (
                SELECT t.TemplatePath FROM DwgTemplates t
                INNER JOIN ProductTypes pt ON t.ProductTypeId = pt.Id
                WHERE pt.ProductGroupId = ProductGroups.Id AND t.TemplateType = 'Cartiglio'
                LIMIT 1
            ) WHERE (CartiglioPath IS NULL OR CartiglioPath = '') AND EXISTS (
                SELECT 1 FROM DwgTemplates t
                INNER JOIN ProductTypes pt ON t.ProductTypeId = pt.Id
                WHERE pt.ProductGroupId = ProductGroups.Id AND t.TemplateType = 'Cartiglio'
            )");

        db.Execute("DELETE FROM DwgTemplates WHERE TemplateType = 'Cartiglio'");

        void AddColumnIfMissing(string tableName, string columnName, string definition)
        {
            var columns = db.Query<string>($"SELECT name FROM pragma_table_info('{tableName}')");
            if (!columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
                db.Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}");
        }
    }
}

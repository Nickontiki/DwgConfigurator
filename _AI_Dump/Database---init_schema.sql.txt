PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS ProductGroups (
    Id              INTEGER PRIMARY KEY,
    Name            TEXT    NOT NULL,
    Description     TEXT    DEFAULT '',
    CartiglioPath   TEXT    DEFAULT ''
);

CREATE TABLE IF NOT EXISTS Modules (
    Id    INTEGER PRIMARY KEY,
    Name  TEXT NOT NULL,
    Sigla TEXT NOT NULL
);

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

CREATE TABLE IF NOT EXISTS DwgTemplates (
    Id              INTEGER PRIMARY KEY,
    ProductTypeId   INTEGER NOT NULL,
    TemplatePath    TEXT    NOT NULL,
    TemplateType    TEXT    NOT NULL CHECK(TemplateType IN ('Cartiglio','Layout')),
    Format          TEXT    DEFAULT '',
    Scale           TEXT    DEFAULT '',
    FOREIGN KEY (ProductTypeId) REFERENCES ProductTypes(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS FixedAttributes (
    Id              INTEGER PRIMARY KEY,
    ProductTypeId   INTEGER NOT NULL,
    AttributeTag    TEXT    NOT NULL,
    FixedValue      TEXT    DEFAULT '',
    AppliesTo       TEXT    NOT NULL DEFAULT 'Cartiglio' CHECK(AppliesTo IN ('Cartiglio','Layout')),
    FOREIGN KEY (ProductTypeId) REFERENCES ProductTypes(Id) ON DELETE CASCADE
);

INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (1, 'Modulo motore',    'MM');
INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (2, 'Modulo biochange', 'MB');
INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (3, 'Modulo ausiliari', 'MA');
INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (4, 'Skid biogas',      'SB');
INSERT OR IGNORE INTO Modules (Id, Name, Sigla) VALUES (5, 'Compressore',      'CG1');

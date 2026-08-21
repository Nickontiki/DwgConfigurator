# DWG Configurator

Soluzione .NET 9 / WPF per la generazione automatica di file DWG con cartiglio configurabile.

## Progetti

| Progetto | Descrizione |
|---|---|
| **DwgConfigurator.Shared** | Modelli, accesso DB (Dapper/SQLite), DwgEngine (ACadSharp), AttributeResolver |
| **DwgConfigurator.ConfiguratorApp** | Interfaccia operativa: commessa, selezione prodotto, form attributi, anteprima cartiglio, generazione DWG |
| **DwgConfigurator.AdminApp** | Gestione configurazioni: CRUD tipi prodotto, DWG template, attributi fissi, attributi blocchi |

## Setup

1. Aprire `DwgConfigurator.sln` in Visual Studio 2022+
2. Ripristinare i pacchetti NuGet
3. Eseguire `Database/init_schema.sql` per creare il DB SQLite (oppure l'app lo crea automaticamente al primo avvio)
4. Configurare il percorso del DB in `AppSettings.cs` se necessario
5. Avviare `AdminApp` per configurare prodotti e template
6. Avviare `ConfiguratorApp` per generare i DWG

## Stack Tecnologico
.NET 9 · WPF · ACadSharp · Dapper · SQLite

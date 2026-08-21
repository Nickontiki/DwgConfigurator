using System.Data;
using Dapper;

namespace DwgConfigurator.Shared.Data;

/// <summary>
/// Trova il primo Id disponibile (gap-filling) in una tabella.
/// </summary>
public static class IdHelper
{
    public static int GetNextAvailableId(IDbConnection db, string tableName)
    {
        var ids = db.Query<int>($"SELECT Id FROM [{tableName}] ORDER BY Id").ToList();
        int nextId = 1;
        foreach (var id in ids)
        {
            if (id != nextId) return nextId;
            nextId++;
        }
        return nextId;
    }
}

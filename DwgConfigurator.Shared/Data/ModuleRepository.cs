using Dapper;
using DwgConfigurator.Shared.Models;

namespace DwgConfigurator.Shared.Data;

public class ModuleRepository
{
    public IEnumerable<ModuleInfo> GetAll()
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<ModuleInfo>("SELECT * FROM Modules ORDER BY Name");
    }

    public ModuleInfo? GetById(int id)
    {
        using var db = DbConnectionFactory.Create();
        return db.QueryFirstOrDefault<ModuleInfo>("SELECT * FROM Modules WHERE Id = @Id", new { Id = id });
    }

    public int Insert(ModuleInfo item)
    {
        using var db = DbConnectionFactory.Create();
        var newId = IdHelper.GetNextAvailableId(db, "Modules");
        db.Execute("INSERT INTO Modules (Id, Name, Sigla) VALUES (@Id, @Name, @Sigla)", new { Id = newId, item.Name, item.Sigla });
        return newId;
    }

    public void Delete(int id)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("DELETE FROM Modules WHERE Id = @Id", new { Id = id });
    }
}

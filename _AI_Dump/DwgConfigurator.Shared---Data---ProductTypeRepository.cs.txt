using Dapper;
using DwgConfigurator.Shared.Models;

namespace DwgConfigurator.Shared.Data;

public class ProductTypeRepository
{
    public IEnumerable<ProductType> GetAll()
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<ProductType>(@"
            SELECT * FROM ProductTypes
            ORDER BY ProductGroupId, ModuleId, Famiglia, Carpenteria, Temperatura, Prodotto, Taglia");
    }

    public IEnumerable<ProductType> GetByGroupId(int groupId)
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<ProductType>(@"
            SELECT * FROM ProductTypes
            WHERE ProductGroupId = @Gid
            ORDER BY ModuleId, Famiglia, Carpenteria, Temperatura, Prodotto, Taglia",
            new { Gid = groupId });
    }

    public IEnumerable<ProductType> GetByGroupAndModuleId(int groupId, int moduleId)
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<ProductType>(@"
            SELECT * FROM ProductTypes
            WHERE ProductGroupId = @Gid AND ModuleId = @Mid
            ORDER BY Famiglia, Carpenteria, Temperatura, Prodotto, Taglia",
            new { Gid = groupId, Mid = moduleId });
    }

    public bool ExistsForModule(int moduleId)
    {
        using var db = DbConnectionFactory.Create();
        return db.ExecuteScalar<int>("SELECT COUNT(1) FROM ProductTypes WHERE ModuleId = @Mid", new { Mid = moduleId }) > 0;
    }

    public ProductType? GetById(int id)
    {
        using var db = DbConnectionFactory.Create();
        return db.QueryFirstOrDefault<ProductType>("SELECT * FROM ProductTypes WHERE Id = @Id", new { Id = id });
    }

    public int Insert(ProductType item)
    {
        using var db = DbConnectionFactory.Create();
        var newId = IdHelper.GetNextAvailableId(db, "ProductTypes");
        if (string.IsNullOrWhiteSpace(item.Prodotto)) item.Prodotto = item.Famiglia;
        if (string.IsNullOrWhiteSpace(item.Taglia)) item.Taglia = item.Temperatura;

        db.Execute(@"INSERT INTO ProductTypes
            (Id, ProductGroupId, ModuleId, Prodotto, Taglia, Famiglia, Carpenteria, Temperatura)
            VALUES
            (@Id, @ProductGroupId, @ModuleId, @Prodotto, @Taglia, @Famiglia, @Carpenteria, @Temperatura)",
            new { Id = newId, item.ProductGroupId, item.ModuleId, item.Prodotto, item.Taglia, item.Famiglia, item.Carpenteria, item.Temperatura });
        return newId;
    }

    public void Update(ProductType item)
    {
        using var db = DbConnectionFactory.Create();
        if (string.IsNullOrWhiteSpace(item.Prodotto)) item.Prodotto = item.Famiglia;
        if (string.IsNullOrWhiteSpace(item.Taglia)) item.Taglia = item.Temperatura;

        db.Execute(@"UPDATE ProductTypes
            SET ProductGroupId=@ProductGroupId,
                ModuleId=@ModuleId,
                Prodotto=@Prodotto,
                Taglia=@Taglia,
                Famiglia=@Famiglia,
                Carpenteria=@Carpenteria,
                Temperatura=@Temperatura
            WHERE Id = @Id", item);
    }

    public void Delete(int id)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("DELETE FROM ProductTypes WHERE Id = @Id", new { Id = id });
    }
}

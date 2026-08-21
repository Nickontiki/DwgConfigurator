using Dapper;
using DwgConfigurator.Shared.Config;
using DwgConfigurator.Shared.Models;

namespace DwgConfigurator.Shared.Data;

public class ProductGroupRepository
{
    public IEnumerable<ProductGroup> GetAll()
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<ProductGroup>("SELECT * FROM ProductGroups ORDER BY Name")
                 .Select(ResolvePaths)
                 .ToList();
    }

    public ProductGroup? GetById(int id)
    {
        using var db = DbConnectionFactory.Create();
        var item = db.QueryFirstOrDefault<ProductGroup>(
            "SELECT * FROM ProductGroups WHERE Id = @Id", new { Id = id });
        return item == null ? null : ResolvePaths(item);
    }

    public ProductGroup? GetByProductTypeId(int productTypeId)
    {
        using var db = DbConnectionFactory.Create();
        var item = db.QueryFirstOrDefault<ProductGroup>(@"
            SELECT g.* FROM ProductGroups g
            INNER JOIN ProductTypes pt ON pt.ProductGroupId = g.Id
            WHERE pt.Id = @Pid", new { Pid = productTypeId });
        return item == null ? null : ResolvePaths(item);
    }

    public int Insert(ProductGroup item)
    {
        using var db = DbConnectionFactory.Create();
        var newId = IdHelper.GetNextAvailableId(db, "ProductGroups");
        db.Execute(@"INSERT INTO ProductGroups (Id, Name, Description, CartiglioPath)
                     VALUES (@Id, @Name, @Desc, @Crt)",
            new
            {
                Id = newId,
                item.Name,
                Desc = item.Description,
                Crt = PortablePathResolver.ToPortablePath(item.CartiglioPath)
            });
        return newId;
    }

    public void Update(ProductGroup item)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute(@"UPDATE ProductGroups
                     SET Name = @Name, Description = @Description, CartiglioPath = @CartiglioPath
                     WHERE Id = @Id",
            new
            {
                item.Id,
                item.Name,
                item.Description,
                CartiglioPath = PortablePathResolver.ToPortablePath(item.CartiglioPath)
            });
    }

    public void Delete(int id)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("DELETE FROM ProductGroups WHERE Id = @Id", new { Id = id });
    }

    private static ProductGroup ResolvePaths(ProductGroup item)
    {
        item.CartiglioPath = PortablePathResolver.Resolve(item.CartiglioPath);
        return item;
    }
}

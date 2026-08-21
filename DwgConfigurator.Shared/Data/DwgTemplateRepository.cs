using Dapper;
using DwgConfigurator.Shared.Config;
using DwgConfigurator.Shared.Models;

namespace DwgConfigurator.Shared.Data;

public class DwgTemplateRepository
{
    public IEnumerable<DwgTemplate> GetAll()
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<DwgTemplate>("SELECT * FROM DwgTemplates ORDER BY ProductTypeId, TemplateType")
                 .Select(ResolvePaths)
                 .ToList();
    }

    public IEnumerable<DwgTemplate> GetByProductTypeId(int productTypeId)
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<DwgTemplate>(
            "SELECT * FROM DwgTemplates WHERE ProductTypeId = @Pid", new { Pid = productTypeId })
            .Select(ResolvePaths)
            .ToList();
    }

    public DwgTemplate? GetLayout(int productTypeId)
    {
        using var db = DbConnectionFactory.Create();
        var item = db.QueryFirstOrDefault<DwgTemplate>(
            "SELECT * FROM DwgTemplates WHERE ProductTypeId = @Pid AND TemplateType = 'Layout'",
            new { Pid = productTypeId });
        return item == null ? null : ResolvePaths(item);
    }

    public int Insert(DwgTemplate item)
    {
        using var db = DbConnectionFactory.Create();
        var newId = IdHelper.GetNextAvailableId(db, "DwgTemplates");
        db.Execute(@"INSERT INTO DwgTemplates (Id, ProductTypeId, TemplatePath, TemplateType, Format, Scale)
                     VALUES (@Id, @ProductTypeId, @TemplatePath, @TemplateType, @Format, @Scale)",
            new
            {
                Id = newId,
                item.ProductTypeId,
                TemplatePath = PortablePathResolver.ToPortablePath(item.TemplatePath),
                item.TemplateType,
                item.Format,
                item.Scale
            });
        return newId;
    }

    public void Update(DwgTemplate item)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute(@"UPDATE DwgTemplates
                     SET ProductTypeId=@ProductTypeId, TemplatePath=@TemplatePath,
                         TemplateType=@TemplateType, Format=@Format, Scale=@Scale
                     WHERE Id = @Id",
            new
            {
                item.Id,
                item.ProductTypeId,
                TemplatePath = PortablePathResolver.ToPortablePath(item.TemplatePath),
                item.TemplateType,
                item.Format,
                item.Scale
            });
    }

    public void Delete(int id)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("DELETE FROM DwgTemplates WHERE Id = @Id", new { Id = id });
    }

    private static DwgTemplate ResolvePaths(DwgTemplate item)
    {
        item.TemplatePath = PortablePathResolver.Resolve(item.TemplatePath);
        return item;
    }
}

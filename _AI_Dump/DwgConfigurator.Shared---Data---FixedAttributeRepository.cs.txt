using Dapper;
using DwgConfigurator.Shared.Models;

namespace DwgConfigurator.Shared.Data;

public class FixedAttributeRepository
{
    public IEnumerable<FixedAttribute> GetByProductTypeId(int productTypeId, string appliesTo)
    {
        using var db = DbConnectionFactory.Create();
        return db.Query<FixedAttribute>(
            "SELECT * FROM FixedAttributes WHERE ProductTypeId = @Pid AND AppliesTo = @At ORDER BY AttributeTag",
            new { Pid = productTypeId, At = appliesTo });
    }

    public Dictionary<string, string> GetDictionaryByProductTypeId(int productTypeId, string appliesTo)
    {
        var list = GetByProductTypeId(productTypeId, appliesTo);
        var dict = new Dictionary<string, string>();
        foreach (var attr in list)
            dict[attr.AttributeTag] = attr.FixedValue ?? string.Empty;
        return dict;
    }

    public int Insert(FixedAttribute item)
    {
        using var db = DbConnectionFactory.Create();
        var newId = IdHelper.GetNextAvailableId(db, "FixedAttributes");
        db.Execute(@"INSERT INTO FixedAttributes (Id, ProductTypeId, AttributeTag, FixedValue, AppliesTo)
            VALUES (@Id, @ProductTypeId, @AttributeTag, @FixedValue, @AppliesTo)",
            new { Id = newId, item.ProductTypeId, item.AttributeTag, item.FixedValue, item.AppliesTo });
        return newId;
    }

    public void Update(FixedAttribute item)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("UPDATE FixedAttributes SET AttributeTag=@AttributeTag, FixedValue=@FixedValue WHERE Id=@Id", item);
    }

    public void Delete(int id)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("DELETE FROM FixedAttributes WHERE Id = @Id", new { Id = id });
    }

    public void DeleteByProductTypeId(int productTypeId, string appliesTo)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("DELETE FROM FixedAttributes WHERE ProductTypeId = @Pid AND AppliesTo = @At",
            new { Pid = productTypeId, At = appliesTo });
    }

    public void BulkUpsert(int productTypeId, string appliesTo, Dictionary<string, string> attributes)
    {
        using var db = DbConnectionFactory.Create();
        db.Execute("DELETE FROM FixedAttributes WHERE ProductTypeId = @Pid AND AppliesTo = @At",
            new { Pid = productTypeId, At = appliesTo });
        foreach (var kv in attributes)
        {
            var newId = IdHelper.GetNextAvailableId(db, "FixedAttributes");
            db.Execute(@"INSERT INTO FixedAttributes (Id, ProductTypeId, AttributeTag, FixedValue, AppliesTo)
                VALUES (@Id, @Pid, @Tag, @Val, @At)",
                new { Id = newId, Pid = productTypeId, Tag = kv.Key, Val = kv.Value, At = appliesTo });
        }
    }

    public void MergeAttributes(int productTypeId, string appliesTo, Dictionary<string, string> attributes)
    {
        var existing = GetDictionaryByProductTypeId(productTypeId, appliesTo);
        using var db = DbConnectionFactory.Create();
        foreach (var kv in attributes)
        {
            if (existing.ContainsKey(kv.Key))
            {
                db.Execute("UPDATE FixedAttributes SET FixedValue=@Val WHERE ProductTypeId=@Pid AND AppliesTo=@At AND AttributeTag=@Tag",
                    new { Val = kv.Value, Pid = productTypeId, At = appliesTo, Tag = kv.Key });
            }
            else
            {
                var newId = IdHelper.GetNextAvailableId(db, "FixedAttributes");
                db.Execute(@"INSERT INTO FixedAttributes (Id, ProductTypeId, AttributeTag, FixedValue, AppliesTo)
                    VALUES (@Id, @Pid, @Tag, @Val, @At)",
                    new { Id = newId, Pid = productTypeId, Tag = kv.Key, Val = kv.Value, At = appliesTo });
            }
        }
    }
}

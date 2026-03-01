using Pondhawk.Persistence.Core.Ddl;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Ddl;

public class PostgreSqlDdlGeneratorTests
{
    private readonly IDdlGenerator _generator = DdlGeneratorFactory.Create("postgresql");

    [Fact]
    public void CreateTypeAsEnum()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "OrderStatus", Note = "Order lifecycle", Values = [new() { Name = "Pending" }, new() { Name = "Shipped" }, new() { Name = "Delivered" }] }
        };
        var ddl = _generator.Generate([], enums);
        ddl.ShouldContain("CREATE TYPE \"OrderStatus\" AS ENUM ('Pending', 'Shipped', 'Delivered')");
        ddl.ShouldContain("-- Order lifecycle");
    }

    [Fact]
    public void DoubleQuoteIdentifiers()
    {
        var model = new Model
        {
            Name = "Users",
            Schema = "public",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }],
            PrimaryKey = new PrimaryKeyInfo { Name = "PK_Users", Columns = ["Id"] }
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("\"Users\"");
        ddl.ShouldContain("\"Id\"");
        ddl.ShouldContain("\"public\"");
    }

    [Fact]
    public void GeneratedAlwaysAsIdentity()
    {
        var model = new Model
        {
            Name = "Users",
            Schema = "public",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true, IsIdentity = true }],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] }
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("GENERATED ALWAYS AS IDENTITY");
    }

    [Fact]
    public void BooleanType()
    {
        var model = new Model
        {
            Name = "Settings",
            Schema = "public",
            Attributes = [new Attribute { Name = "IsActive", DataType = "boolean" }]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("boolean");
    }

    [Fact]
    public void EnumColumnUsesTypeName()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "OrderStatus", Values = [new() { Name = "Pending" }, new() { Name = "Shipped" }] }
        };
        var model = new Model
        {
            Name = "Orders",
            Schema = "public",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "Status", DataType = "OrderStatus" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] }
        };
        var ddl = _generator.Generate([model], enums);
        ddl.ShouldContain("CREATE TYPE \"OrderStatus\" AS ENUM");
        ddl.ShouldContain("\"Status\" \"OrderStatus\"");
    }

    [Fact]
    public void DependencyOrdering()
    {
        var categories = new Model
        {
            Name = "Categories",
            Schema = "public",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] }
        };
        var products = new Model
        {
            Name = "Products",
            Schema = "public",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "CategoryId", DataType = "int" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] },
            ForeignKeys = [new ForeignKey { Columns = ["CategoryId"], PrincipalTable = "Categories", PrincipalColumns = ["Id"] }]
        };

        // Pass products before categories — generator should sort them
        var ddl = _generator.Generate([products, categories]);
        var catIdx = ddl.IndexOf("CREATE TABLE \"public\".\"Categories\"");
        var prodIdx = ddl.IndexOf("CREATE TABLE \"public\".\"Products\"");
        catIdx.ShouldBeLessThan(prodIdx);
    }

    [Fact]
    public void TypeMappings()
    {
        var model = new Model
        {
            Name = "Test",
            Schema = "public",
            Attributes =
            [
                new Attribute { Name = "Col1", DataType = "int" },
                new Attribute { Name = "Col2", DataType = "uuid" },
                new Attribute { Name = "Col3", DataType = "json" }
            ]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("integer");
        ddl.ShouldContain("uuid");
        ddl.ShouldContain("jsonb");
    }
}

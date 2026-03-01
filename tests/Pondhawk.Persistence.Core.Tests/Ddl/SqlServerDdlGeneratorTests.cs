using Pondhawk.Persistence.Core.Ddl;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Ddl;

public class SqlServerDdlGeneratorTests
{
    private readonly IDdlGenerator _generator = DdlGeneratorFactory.Create("sqlserver");

    private static Model CreateTable(string name, string schema = "dbo") => new()
    {
        Name = name,
        Schema = schema,
        Attributes =
        [
            new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true, IsIdentity = true },
            new Attribute { Name = "Name", DataType = "varchar(100)" }
        ],
        PrimaryKey = new PrimaryKeyInfo { Name = $"PK_{name}", Columns = ["Id"] }
    };

    [Fact]
    public void GeneratesCreateTable()
    {
        var models = new List<Model> { CreateTable("Products") };
        var ddl = _generator.Generate(models);

        ddl.ShouldContain("CREATE TABLE [dbo].[Products]");
        ddl.ShouldContain("[Id] int NOT NULL IDENTITY(1,1)");
        ddl.ShouldContain("[Name] varchar(100) NOT NULL");
        ddl.ShouldContain("CONSTRAINT [PK_Products] PRIMARY KEY ([Id])");
    }

    [Fact]
    public void BracketQuoting()
    {
        var models = new List<Model> { CreateTable("Order Details") };
        var ddl = _generator.Generate(models);
        ddl.ShouldContain("[Order Details]");
    }

    [Fact]
    public void Identity()
    {
        var models = new List<Model> { CreateTable("Users") };
        var ddl = _generator.Generate(models);
        ddl.ShouldContain("IDENTITY(1,1)");
    }

    [Fact]
    public void EnumAsCheckComment()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "OrderStatus", Values = [new() { Name = "Pending" }, new() { Name = "Shipped" }] }
        };
        var ddl = _generator.Generate([], enums);
        ddl.ShouldContain("Enum: OrderStatus");
        ddl.ShouldContain("Pending, Shipped");
    }

    [Fact]
    public void EnumCheckConstraint()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "OrderStatus", Values = [new() { Name = "Pending" }, new() { Name = "Shipped" }] }
        };
        var model = new Model
        {
            Name = "Orders",
            Schema = "dbo",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "Status", DataType = "OrderStatus" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] }
        };
        var ddl = _generator.Generate([model], enums);
        ddl.ShouldContain("ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [CHK_Orders_Status] CHECK ([Status] IN ('Pending', 'Shipped'))");
        // Enum column should be mapped to varchar(255)
        ddl.ShouldContain("[Status] varchar(255)");
    }

    [Fact]
    public void NotNullColumns()
    {
        var model = new Model
        {
            Name = "Users",
            Schema = "dbo",
            Attributes =
            [
                new Attribute { Name = "Email", DataType = "varchar(255)", IsNullable = false },
                new Attribute { Name = "Bio", DataType = "text", IsNullable = true }
            ]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("[Email] varchar(255) NOT NULL");
        ddl.ShouldNotContain("[Bio] text NOT NULL");
    }

    [Fact]
    public void Indexes()
    {
        var model = CreateTable("Users");
        model.Indexes = [new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"], IsUnique = true }];
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("CREATE UNIQUE INDEX [IX_Users_Name] ON [dbo].[Users] ([Name] ASC)");
    }

    [Fact]
    public void ForeignKeys()
    {
        var products = CreateTable("Products");
        var orders = CreateTable("Orders");
        orders.Attributes.Add(new Attribute { Name = "ProductId", DataType = "int" });
        orders.ForeignKeys =
        [
            new ForeignKey
            {
                Name = "FK_Orders_Products",
                Columns = ["ProductId"],
                PrincipalTable = "Products",
                PrincipalSchema = "dbo",
                PrincipalColumns = ["Id"],
                OnDelete = "Cascade"
            }
        ];

        var ddl = _generator.Generate([products, orders]);
        ddl.ShouldContain("ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [FK_Orders_Products]");
        ddl.ShouldContain("FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([Id])");
        ddl.ShouldContain("ON DELETE CASCADE");
    }

    [Fact]
    public void ForeignKeys_OnUpdate()
    {
        var products = CreateTable("Products");
        var orders = CreateTable("Orders");
        orders.Attributes.Add(new Attribute { Name = "ProductId", DataType = "int" });
        orders.ForeignKeys =
        [
            new ForeignKey
            {
                Name = "FK_Orders_Products",
                Columns = ["ProductId"],
                PrincipalTable = "Products",
                PrincipalSchema = "dbo",
                PrincipalColumns = ["Id"],
                OnUpdate = "Cascade"
            }
        ];

        var ddl = _generator.Generate([products, orders]);
        ddl.ShouldContain("ON UPDATE CASCADE");
    }

    [Fact]
    public void Notes()
    {
        var model = CreateTable("Users");
        model.Note = "Application users table";
        model.Attributes[1].Note = "Display name";
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("-- Application users table");
        ddl.ShouldContain("/* Display name */");
    }

    [Fact]
    public void AutoGeneratedConstraintNames()
    {
        var model = new Model
        {
            Name = "Orders",
            Schema = "dbo",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "CustomerId", DataType = "int" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] },
            ForeignKeys =
            [
                new ForeignKey { Columns = ["CustomerId"], PrincipalTable = "Customers", PrincipalColumns = ["Id"] }
            ],
            Indexes =
            [
                new IndexInfo { Columns = ["CustomerId"] }
            ]
        };

        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("PK_Orders");
        ddl.ShouldContain("FK_Orders_Customers");
        ddl.ShouldContain("IX_Orders_CustomerId");
    }

    [Fact]
    public void CircularForeignKeys()
    {
        var tableA = new Model
        {
            Name = "TableA",
            Schema = "dbo",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "TableBId", DataType = "int" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] },
            ForeignKeys = [new ForeignKey { Columns = ["TableBId"], PrincipalTable = "TableB", PrincipalColumns = ["Id"] }]
        };
        var tableB = new Model
        {
            Name = "TableB",
            Schema = "dbo",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "TableAId", DataType = "int" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] },
            ForeignKeys = [new ForeignKey { Columns = ["TableAId"], PrincipalTable = "TableA", PrincipalColumns = ["Id"] }]
        };

        var ddl = _generator.Generate([tableA, tableB]);
        // Both tables should be created
        ddl.ShouldContain("CREATE TABLE [dbo].[TableA]");
        ddl.ShouldContain("CREATE TABLE [dbo].[TableB]");
        // Both FKs should be emitted as ALTER TABLE (after both tables exist)
        ddl.ShouldContain("FOREIGN KEY ([TableBId]) REFERENCES [dbo].[TableB]");
        ddl.ShouldContain("FOREIGN KEY ([TableAId]) REFERENCES [dbo].[TableA]");
        // CREATE TABLE should come before ALTER TABLE
        var createIdx = ddl.IndexOf("CREATE TABLE");
        var alterIdx = ddl.IndexOf("ALTER TABLE");
        createIdx.ShouldBeLessThan(alterIdx);
    }

    [Fact]
    public void EmptySchema_ProducesValidDdl()
    {
        var ddl = _generator.Generate([]);
        ddl.ShouldContain("Generated by pondhawk-mcp for pondhawk-mcp");
    }

    [Fact]
    public void HeaderIncludesProjectName()
    {
        var ddl = _generator.Generate([], projectName: "my-project");
        ddl.ShouldContain("Generated by pondhawk-mcp for my-project");
    }

    [Fact]
    public void HeaderIncludesDescription()
    {
        var ddl = _generator.Generate([], projectName: "my-project", description: "Accounting database");
        ddl.ShouldContain("Generated by pondhawk-mcp for my-project");
        ddl.ShouldContain("-- Accounting database");
    }

    [Fact]
    public void HeaderOmitsDescriptionWhenNull()
    {
        var ddl = _generator.Generate([]);
        var lines = ddl.Split('\n').Select(l => l.TrimEnd()).ToArray();
        // Second line should be blank (no description line)
        lines[1].ShouldBe("");
    }

    [Fact]
    public void DefaultValue()
    {
        var model = new Model
        {
            Name = "Config",
            Schema = "dbo",
            Attributes =
            [
                new Attribute { Name = "IsActive", DataType = "bit", DefaultValue = "1" }
            ]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("DEFAULT 1");
    }

    [Fact]
    public void ViewsExcludedFromDdl()
    {
        var table = CreateTable("Users");
        var view = new Model
        {
            Name = "ActiveUsers",
            Schema = "dbo",
            IsView = true,
            Attributes = [new Attribute { Name = "Id", DataType = "int" }, new Attribute { Name = "Name", DataType = "varchar(100)" }]
        };
        var ddl = _generator.Generate([table, view]);
        ddl.ShouldContain("CREATE TABLE [dbo].[Users]");
        ddl.ShouldNotContain("ActiveUsers");
    }

    [Fact]
    public void PkBackingIndex_ExcludedFromDdl()
    {
        var model = CreateTable("Users");
        model.Indexes =
        [
            new IndexInfo { Name = "PK_Users", Columns = ["Id"], IsUnique = true }
        ];
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("CONSTRAINT [PK_Users] PRIMARY KEY ([Id])");
        ddl.ShouldNotContain("CREATE UNIQUE INDEX [PK_Users]");
        ddl.ShouldNotContain("CREATE INDEX [PK_Users]");
    }

    [Fact]
    public void DependencyOrdering()
    {
        var categories = CreateTable("Categories");
        var products = CreateTable("Products");
        products.ForeignKeys =
        [
            new ForeignKey { Columns = ["CategoryId"], PrincipalTable = "Categories", PrincipalColumns = ["Id"] }
        ];
        products.Attributes.Add(new Attribute { Name = "CategoryId", DataType = "int" });

        // Pass products before categories — generator should sort them
        var ddl = _generator.Generate([products, categories]);
        var catIdx = ddl.IndexOf("CREATE TABLE [dbo].[Categories]");
        var prodIdx = ddl.IndexOf("CREATE TABLE [dbo].[Products]");
        catIdx.ShouldBeLessThan(prodIdx);
    }
}

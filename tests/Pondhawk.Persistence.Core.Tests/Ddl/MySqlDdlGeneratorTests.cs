using Pondhawk.Persistence.Core.Ddl;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Ddl;

public class MySqlDdlGeneratorTests
{
    private readonly IDdlGenerator _generator = DdlGeneratorFactory.Create("mysql");

    [Fact]
    public void AutoIncrement()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true, IsIdentity = true }],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] }
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("AUTO_INCREMENT");
    }

    [Fact]
    public void BacktickQuoting()
    {
        var model = new Model
        {
            Name = "Order Items",
            Attributes = [new Attribute { Name = "Id", DataType = "int" }]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("`Order Items`");
        ddl.ShouldContain("`Id`");
    }

    [Fact]
    public void InlineEnumComment()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "Status", Values = [new() { Name = "Active" }, new() { Name = "Inactive" }] }
        };
        var ddl = _generator.Generate([], enums);
        ddl.ShouldContain("Enum: Status");
        ddl.ShouldContain("Active, Inactive");
    }

    [Fact]
    public void InlineEnumColumnType()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "Status", Values = [new() { Name = "Active" }, new() { Name = "Inactive" }] }
        };
        var model = new Model
        {
            Name = "Users",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "UserStatus", DataType = "Status" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] }
        };
        var ddl = _generator.Generate([model], enums);
        ddl.ShouldContain("ENUM('Active', 'Inactive')");
    }

    [Fact]
    public void EngineInnoDB()
    {
        var model = new Model
        {
            Name = "Products",
            Attributes = [new Attribute { Name = "Id", DataType = "int" }],
            PrimaryKey = new PrimaryKeyInfo { Columns = ["Id"] }
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("ENGINE = INNODB");
    }

    [Fact]
    public void BooleanAsTinyint()
    {
        var model = new Model
        {
            Name = "Settings",
            Attributes = [new Attribute { Name = "IsActive", DataType = "boolean" }]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("tinyint(1)");
    }

    [Fact]
    public void DefaultDatetimeLiteral_WrappedInQuotes()
    {
        var model = new Model
        {
            Name = "Events",
            Attributes = [new Attribute { Name = "CreatedAt", DataType = "datetime", DefaultValue = "2024-01-01 00:00:00" }]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("DEFAULT '2024-01-01 00:00:00'");
    }

    [Fact]
    public void DefaultDatetimeFunction_NotWrappedInQuotes()
    {
        var model = new Model
        {
            Name = "Events",
            Attributes = [new Attribute { Name = "CreatedAt", DataType = "datetime", DefaultValue = "NOW()" }]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("DEFAULT NOW()");
        ddl.ShouldNotContain("DEFAULT 'NOW()'");
    }

    [Fact]
    public void DefaultCurrentTimestamp_NotWrappedInQuotes()
    {
        var model = new Model
        {
            Name = "Events",
            Attributes = [new Attribute { Name = "CreatedAt", DataType = "timestamp", DefaultValue = "CURRENT_TIMESTAMP" }]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("DEFAULT CURRENT_TIMESTAMP");
        ddl.ShouldNotContain("DEFAULT 'CURRENT_TIMESTAMP'");
    }

    [Fact]
    public void PkBackingIndex_ExcludedFromDdl()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true, IsIdentity = true },
                new Attribute { Name = "Name", DataType = "varchar(100)" }
            ],
            PrimaryKey = new PrimaryKeyInfo { Name = "PK_Users", Columns = ["Id"] },
            Indexes =
            [
                new IndexInfo { Name = "PK_Users", Columns = ["Id"], IsUnique = true }
            ]
        };
        var ddl = _generator.Generate([model]);
        ddl.ShouldContain("PRIMARY KEY");
        ddl.ShouldNotContain("CREATE UNIQUE INDEX");
        ddl.ShouldNotContain("CREATE INDEX `PK_Users`");
    }

    [Fact]
    public void MariaDbFactory()
    {
        var generator = DdlGeneratorFactory.Create("mariadb");
        generator.ShouldBeOfType<MySqlDdlGenerator>();
    }
}

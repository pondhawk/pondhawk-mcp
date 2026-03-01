using Pondhawk.Persistence.Core.Migrations;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Migrations;

public class MigrationSqlGeneratorTests
{
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

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void TableAdded_GeneratesCreateTable(string provider)
    {
        var model = CreateTable("Users");
        var changes = new List<SchemaChange> { new TableAdded("Users", "dbo", model) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("CREATE TABLE");
        results[0].Sql.ShouldContain("Users");
    }

    // SQLite does not support separate CREATE FOREIGN KEY statements
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void TableAdded_WithIndexesAndFks(string provider)
    {
        var model = CreateTable("Orders");
        model.Indexes.Add(new IndexInfo { Name = "IX_Orders_Name", Columns = ["Name"] });
        model.ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"]
        });
        model.Attributes.Add(new Attribute { Name = "UserId", DataType = "int" });

        var changes = new List<SchemaChange> { new TableAdded("Orders", "dbo", model) };
        var results = MigrationSqlGenerator.Generate(provider, changes);

        // Should have CREATE TABLE + CREATE INDEX + FK
        results.Count.ShouldBeGreaterThanOrEqualTo(3);
        results.ShouldContain(r => r.Sql.Contains("CREATE TABLE"));
        results.ShouldContain(r => r.Sql.Contains("INDEX"));
        results.ShouldContain(r => r.Sql.Contains("FOREIGN KEY"));
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void TableRemoved_GeneratesDropTable(string provider)
    {
        var changes = new List<SchemaChange> { new TableRemoved("Users", "dbo") };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("DROP TABLE");
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void ColumnAdded_GeneratesAlterTableAddColumn(string provider)
    {
        var attr = new Attribute { Name = "Email", DataType = "varchar(255)" };
        var changes = new List<SchemaChange> { new ColumnAdded("Users", "dbo", attr) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("Email");
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void ColumnRemoved_GeneratesAlterTableDropColumn(string provider)
    {
        var changes = new List<SchemaChange> { new ColumnRemoved("Users", "dbo", "Email") };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("Email");
    }

    // SQLite does not support ALTER COLUMN
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void ColumnModified_GeneratesAlterColumn(string provider)
    {
        var oldAttr = new Attribute { Name = "Name", DataType = "varchar(100)" };
        var newAttr = new Attribute { Name = "Name", DataType = "varchar(200)" };
        var changes = new List<SchemaChange> { new ColumnModified("Users", "dbo", oldAttr, newAttr) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("Name");
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void IndexAdded_GeneratesCreateIndex(string provider)
    {
        var idx = new IndexInfo { Name = "IX_Users_Email", Columns = ["Email"], IsUnique = true };
        var changes = new List<SchemaChange> { new IndexAdded("Users", "dbo", idx) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("INDEX");
        results[0].Sql.ShouldContain("IX_Users_Email");
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void IndexRemoved_GeneratesDropIndex(string provider)
    {
        var idx = new IndexInfo { Name = "IX_Users_Email", Columns = ["Email"] };
        var changes = new List<SchemaChange> { new IndexRemoved("Users", "dbo", idx) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("IX_Users_Email");
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void IndexModified_GeneratesDropAndCreate(string provider)
    {
        var oldIdx = new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"], IsUnique = false };
        var newIdx = new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"], IsUnique = true };
        var changes = new List<SchemaChange> { new IndexModified("Users", "dbo", oldIdx, newIdx) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.Count.ShouldBe(2);
    }

    // SQLite does not support separate CREATE/DROP FOREIGN KEY statements
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void ForeignKeyAdded_GeneratesAddConstraint(string provider)
    {
        var fk = new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnDelete = "Cascade"
        };
        var changes = new List<SchemaChange> { new ForeignKeyAdded("Orders", "dbo", fk) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("FOREIGN KEY");
    }

    // SQLite does not support separate CREATE/DROP FOREIGN KEY statements
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void ForeignKeyRemoved_GeneratesDropConstraint(string provider)
    {
        var fk = new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"]
        };
        var changes = new List<SchemaChange> { new ForeignKeyRemoved("Orders", "dbo", fk) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("FK_Orders_Users");
    }

    // SQLite does not support separate CREATE/DROP FOREIGN KEY statements
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void ForeignKeyModified_GeneratesDropAndCreate(string provider)
    {
        var oldFk = new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnDelete = "NoAction"
        };
        var newFk = new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnDelete = "Cascade"
        };
        var changes = new List<SchemaChange> { new ForeignKeyModified("Orders", "dbo", oldFk, newFk) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.Count.ShouldBe(2);
    }

    // SQLite does not support ALTER/DROP constraint operations
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void PrimaryKeyModified_GeneratesDropAndCreate(string provider)
    {
        var oldPk = new PrimaryKeyInfo { Name = "PK_Users", Columns = ["Id"] };
        var newPk = new PrimaryKeyInfo { Name = "PK_Users", Columns = ["TenantId", "Id"] };
        var changes = new List<SchemaChange> { new PrimaryKeyModified("Users", "dbo", oldPk, newPk) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void ColumnWithDefaultValue(string provider)
    {
        var attr = new Attribute { Name = "IsActive", DataType = "bit", DefaultValue = "1" };
        var changes = new List<SchemaChange> { new ColumnAdded("Users", "dbo", attr) };

        var results = MigrationSqlGenerator.Generate(provider, changes);

        results.ShouldNotBeEmpty();
        results[0].Sql.ShouldContain("1");
    }
}

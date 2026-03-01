using Pondhawk.Persistence.Core.Migrations;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Migrations;

public class MigrationSqlRendererTests
{
    [Fact]
    public void HeaderContainsMigrationName()
    {
        var statements = new List<(SchemaChange, string)>
        {
            (new TableAdded("Users", "dbo", new Model { Name = "Users", Schema = "dbo" }), "CREATE TABLE Users (Id int);")
        };

        var result = MigrationSqlRenderer.Render("add_users_table", "sqlserver", statements, []);

        result.ShouldContain("-- Migration: add_users_table");
    }

    [Fact]
    public void HeaderContainsProvider()
    {
        var statements = new List<(SchemaChange, string)>
        {
            (new TableAdded("Users", "dbo", new Model { Name = "Users", Schema = "dbo" }), "CREATE TABLE Users (Id int);")
        };

        var result = MigrationSqlRenderer.Render("test", "postgresql", statements, []);

        result.ShouldContain("-- Provider:  postgresql");
    }

    [Fact]
    public void HeaderContainsTimestamp()
    {
        var statements = new List<(SchemaChange, string)>
        {
            (new TableAdded("Users", "dbo", new Model { Name = "Users", Schema = "dbo" }), "CREATE TABLE Users (Id int);")
        };

        var result = MigrationSqlRenderer.Render("test", "sqlserver", statements, []);

        result.ShouldContain("-- Generated:");
        result.ShouldContain("UTC");
    }

    [Fact]
    public void ChangeSummaryIncluded()
    {
        var statements = new List<(SchemaChange, string)>
        {
            (new TableAdded("Users", "dbo", new Model { Name = "Users", Schema = "dbo" }), "CREATE TABLE Users;"),
            (new ColumnAdded("Orders", "dbo", new Attribute { Name = "Email", DataType = "varchar(255)" }), "ALTER TABLE Orders ADD Email;")
        };

        var result = MigrationSqlRenderer.Render("test", "sqlserver", statements, []);

        result.ShouldContain("-- Changes:");
        result.ShouldContain("Add table dbo.Users");
        result.ShouldContain("Add column dbo.Orders.Email");
    }

    [Fact]
    public void WarningsIncluded()
    {
        var statements = new List<(SchemaChange, string)>
        {
            (new TableRemoved("OldTable", "dbo"), "DROP TABLE OldTable;")
        };
        var warnings = new List<MigrationWarning>
        {
            new(WarningType.Destructive, "Table dbo.OldTable will be dropped")
        };

        var result = MigrationSqlRenderer.Render("test", "sqlserver", statements, warnings);

        result.ShouldContain("-- Warnings:");
        result.ShouldContain("[Destructive]");
        result.ShouldContain("OldTable will be dropped");
    }

    [Fact]
    public void NoChangesWarning_NotInWarningsSection()
    {
        var warnings = new List<MigrationWarning>
        {
            new(WarningType.NoChanges, "No schema changes detected")
        };

        var result = MigrationSqlRenderer.Render("test", "sqlserver", [], warnings);

        result.ShouldNotContain("-- Warnings:");
    }

    [Fact]
    public void StatementsAreNumbered()
    {
        var change1 = new TableAdded("Users", "dbo", new Model { Name = "Users", Schema = "dbo" });
        var change2 = new ColumnAdded("Orders", "dbo", new Attribute { Name = "Email", DataType = "varchar(255)" });
        var statements = new List<(SchemaChange, string)>
        {
            (change1, "CREATE TABLE Users;"),
            (change2, "ALTER TABLE Orders ADD Email;")
        };

        var result = MigrationSqlRenderer.Render("test", "sqlserver", statements, []);

        result.ShouldContain("-- [1] Add table dbo.Users");
        result.ShouldContain("-- [2] Add column dbo.Orders.Email");
    }

    [Fact]
    public void SemicolonAppendedIfMissing()
    {
        var statements = new List<(SchemaChange, string)>
        {
            (new TableRemoved("Users", "dbo"), "DROP TABLE Users")
        };

        var result = MigrationSqlRenderer.Render("test", "sqlserver", statements, []);

        result.ShouldContain("DROP TABLE Users;");
    }

    [Fact]
    public void SemicolonNotDuplicatedIfPresent()
    {
        var statements = new List<(SchemaChange, string)>
        {
            (new TableRemoved("Users", "dbo"), "DROP TABLE Users;")
        };

        var result = MigrationSqlRenderer.Render("test", "sqlserver", statements, []);

        result.ShouldNotContain("DROP TABLE Users;;");
    }
}

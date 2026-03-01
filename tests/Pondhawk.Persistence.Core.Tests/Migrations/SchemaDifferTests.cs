using Pondhawk.Persistence.Core.Migrations;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Migrations;

public class SchemaDifferTests
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

    [Fact]
    public void EmptyBaseline_Bootstrap()
    {
        var desired = new List<Model> { CreateTable("Users") };
        var (changes, warnings) = SchemaDiffer.Diff([], desired);

        changes.OfType<TableAdded>().ShouldContain(c => c.TableName == "Users");
        warnings.ShouldNotContain(w => w.Type == WarningType.NoChanges);
    }

    [Fact]
    public void NoChanges_ReturnsWarning()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };

        var (changes, warnings) = SchemaDiffer.Diff(baseline, desired);

        changes.ShouldBeEmpty();
        warnings.ShouldContain(w => w.Type == WarningType.NoChanges);
    }

    [Fact]
    public void TableAdded()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users"), CreateTable("Orders") };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<TableAdded>().ShouldContain(c => c.TableName == "Orders");
        changes.ShouldNotContain(c => c.TableName == "Users");
    }

    [Fact]
    public void TableRemoved()
    {
        var baseline = new List<Model> { CreateTable("Users"), CreateTable("Orders") };
        var desired = new List<Model> { CreateTable("Users") };

        var (changes, warnings) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<TableRemoved>().ShouldContain(c => c.TableName == "Orders");
        warnings.ShouldContain(w => w.Type == WarningType.Destructive && w.Message.Contains("Orders"));
    }

    [Fact]
    public void ColumnAdded()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes.Add(new Attribute { Name = "Email", DataType = "varchar(255)" });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnAdded>().ShouldContain(c => c.Column.Name == "Email");
    }

    [Fact]
    public void ColumnRemoved()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Attributes.Add(new Attribute { Name = "Email", DataType = "varchar(255)" });
        var desired = new List<Model> { CreateTable("Users") };

        var (changes, warnings) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnRemoved>().ShouldContain(c => c.ColumnName == "Email");
        warnings.ShouldContain(w => w.Type == WarningType.Destructive && w.Message.Contains("Email"));
    }

    [Fact]
    public void ColumnModified_DataType()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes[1] = new Attribute { Name = "Name", DataType = "nvarchar(200)" };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnModified>().ShouldContain(c => c.NewColumn.Name == "Name");
    }

    [Fact]
    public void ColumnModified_Nullability()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes[1] = new Attribute { Name = "Name", DataType = "varchar(100)", IsNullable = true };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnModified>().ShouldContain(c => c.NewColumn.Name == "Name");
    }

    [Fact]
    public void ColumnModified_DefaultValue()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes[1] = new Attribute { Name = "Name", DataType = "varchar(100)", DefaultValue = "'Unknown'" };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void ColumnModified_MaxLength()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Attributes[1] = new Attribute { Name = "Name", DataType = "varchar", MaxLength = 100 };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes[1] = new Attribute { Name = "Name", DataType = "varchar", MaxLength = 200 };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void ColumnModified_Precision()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Attributes.Add(new Attribute { Name = "Balance", DataType = "decimal", Precision = 18, Scale = 2 });
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes.Add(new Attribute { Name = "Balance", DataType = "decimal", Precision = 10, Scale = 4 });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnModified>().ShouldContain(c => c.NewColumn.Name == "Balance");
    }

    [Fact]
    public void ColumnModified_Identity()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes[0] = new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true, IsIdentity = false };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ColumnModified>().ShouldContain(c => c.NewColumn.Name == "Id");
    }

    [Fact]
    public void DataLossWarning_MaxLengthNarrowed()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Attributes[1] = new Attribute { Name = "Name", DataType = "varchar", MaxLength = 200 };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes[1] = new Attribute { Name = "Name", DataType = "varchar", MaxLength = 100 };

        var (_, warnings) = SchemaDiffer.Diff(baseline, desired);

        warnings.ShouldContain(w => w.Type == WarningType.DataLoss && w.Message.Contains("MaxLength"));
    }

    [Fact]
    public void DataLossWarning_PrecisionNarrowed()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Attributes.Add(new Attribute { Name = "Balance", DataType = "decimal", Precision = 18, Scale = 2 });
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes.Add(new Attribute { Name = "Balance", DataType = "decimal", Precision = 10, Scale = 2 });

        var (_, warnings) = SchemaDiffer.Diff(baseline, desired);

        warnings.ShouldContain(w => w.Type == WarningType.DataLoss && w.Message.Contains("Precision"));
    }

    [Fact]
    public void PossibleRenameWarning()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Attributes.Add(new Attribute { Name = "FirstName", DataType = "varchar(100)" });
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes.Add(new Attribute { Name = "GivenName", DataType = "varchar(100)" });

        var (_, warnings) = SchemaDiffer.Diff(baseline, desired);

        warnings.ShouldContain(w => w.Type == WarningType.PossibleRename
            && w.Message.Contains("FirstName") && w.Message.Contains("GivenName"));
    }

    [Fact]
    public void IndexAdded()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Indexes.Add(new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"], IsUnique = true });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<IndexAdded>().ShouldContain(c => c.Index.Name == "IX_Users_Name");
    }

    [Fact]
    public void IndexRemoved()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Indexes.Add(new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"] });
        var desired = new List<Model> { CreateTable("Users") };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<IndexRemoved>().ShouldContain(c => c.Index.Name == "IX_Users_Name");
    }

    [Fact]
    public void IndexModified_Columns()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Attributes.Add(new Attribute { Name = "Email", DataType = "varchar(255)" });
        baseline[0].Indexes.Add(new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"] });
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes.Add(new Attribute { Name = "Email", DataType = "varchar(255)" });
        desired[0].Indexes.Add(new IndexInfo { Name = "IX_Users_Name", Columns = ["Name", "Email"] });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<IndexModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void IndexModified_Uniqueness()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        baseline[0].Indexes.Add(new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"], IsUnique = false });
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Indexes.Add(new IndexInfo { Name = "IX_Users_Name", Columns = ["Name"], IsUnique = true });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<IndexModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void ForeignKeyAdded()
    {
        var baseline = new List<Model> { CreateTable("Orders") };
        var desired = new List<Model> { CreateTable("Orders") };
        desired[0].ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnDelete = "Cascade"
        });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ForeignKeyAdded>().ShouldContain(c => c.ForeignKey.Name == "FK_Orders_Users");
    }

    [Fact]
    public void ForeignKeyRemoved()
    {
        var baseline = new List<Model> { CreateTable("Orders") };
        baseline[0].ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"]
        });
        var desired = new List<Model> { CreateTable("Orders") };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ForeignKeyRemoved>().ShouldContain(c => c.ForeignKey.Name == "FK_Orders_Users");
    }

    [Fact]
    public void ForeignKeyModified_OnDelete()
    {
        var baseline = new List<Model> { CreateTable("Orders") };
        baseline[0].ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnDelete = "NoAction"
        });
        var desired = new List<Model> { CreateTable("Orders") };
        desired[0].ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnDelete = "Cascade"
        });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ForeignKeyModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void ForeignKeyModified_OnUpdate()
    {
        var baseline = new List<Model> { CreateTable("Orders") };
        baseline[0].ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"]
        });
        var desired = new List<Model> { CreateTable("Orders") };
        desired[0].ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnUpdate = "Cascade"
        });

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<ForeignKeyModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void PrimaryKeyModified()
    {
        var baseline = new List<Model> { CreateTable("Users") };
        var desired = new List<Model> { CreateTable("Users") };
        desired[0].Attributes.Add(new Attribute { Name = "TenantId", DataType = "int" });
        desired[0].PrimaryKey = new PrimaryKeyInfo { Name = "PK_Users", Columns = ["TenantId", "Id"] };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<PrimaryKeyModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void PrimaryKeyAdded()
    {
        var baseline = new List<Model>
        {
            new()
            {
                Name = "Users", Schema = "dbo",
                Attributes = [new Attribute { Name = "Id", DataType = "int" }]
            }
        };
        var desired = new List<Model>
        {
            new()
            {
                Name = "Users", Schema = "dbo",
                Attributes = [new Attribute { Name = "Id", DataType = "int" }],
                PrimaryKey = new PrimaryKeyInfo { Name = "PK_Users", Columns = ["Id"] }
            }
        };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<PrimaryKeyModified>().ShouldNotBeEmpty();
    }

    [Fact]
    public void ViewsExcluded()
    {
        var baseline = new List<Model>();
        var desired = new List<Model>
        {
            CreateTable("Users"),
            new()
            {
                Name = "ActiveUsers", Schema = "dbo", IsView = true,
                Attributes = [new Attribute { Name = "Id", DataType = "int" }]
            }
        };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.ShouldNotContain(c => c.TableName == "ActiveUsers");
        changes.OfType<TableAdded>().ShouldContain(c => c.TableName == "Users");
    }

    [Fact]
    public void MultiSchema()
    {
        var baseline = new List<Model> { CreateTable("Users", "dbo") };
        var desired = new List<Model>
        {
            CreateTable("Users", "dbo"),
            CreateTable("Users", "audit")
        };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        changes.OfType<TableAdded>().ShouldContain(c => c.SchemaName == "audit");
        changes.Count.ShouldBe(1);
    }

    [Fact]
    public void Ordering_DropsBeforeCreates()
    {
        var baseline = new List<Model>
        {
            CreateTable("OldTable")
        };
        var desired = new List<Model>
        {
            CreateTable("NewTable")
        };

        var (changes, _) = SchemaDiffer.Diff(baseline, desired);

        var dropIdx = changes.FindIndex(c => c is TableRemoved);
        var addIdx = changes.FindIndex(c => c is TableAdded);
        dropIdx.ShouldBeLessThan(addIdx);
    }

    [Fact]
    public void Ordering_FkDropsBeforeTableDrops()
    {
        var baseline = new List<Model>
        {
            CreateTable("Orders")
        };
        baseline[0].ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Orders_Users",
            Columns = ["UserId"],
            PrincipalTable = "Users",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"]
        });

        var (changes, _) = SchemaDiffer.Diff(baseline, []);

        var fkDropIdx = changes.FindIndex(c => c is ForeignKeyRemoved);
        var tableDropIdx = changes.FindIndex(c => c is TableRemoved);
        fkDropIdx.ShouldBeLessThan(tableDropIdx);
    }

    [Fact]
    public void Describe_ReturnsReadableText()
    {
        var change = new TableAdded("Users", "dbo", CreateTable("Users"));
        change.Describe().ShouldBe("Add table dbo.Users");

        var removed = new TableRemoved("Orders", "dbo");
        removed.Describe().ShouldBe("Drop table dbo.Orders");
    }
}

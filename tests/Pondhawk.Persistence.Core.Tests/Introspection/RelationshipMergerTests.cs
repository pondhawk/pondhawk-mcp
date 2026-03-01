using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Pondhawk.Persistence.Core.Tests.Fixtures;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Introspection;

public class RelationshipMergerTests
{
    private static Model MakeModel(string name, string schema = "dbo") =>
        new() { Name = name, Schema = schema };

    [Fact]
    public void Merge_ExplicitRelationship_AddsForeignKey()
    {
        var products = MakeModel("Products");
        var categories = MakeModel("Categories");
        var models = new List<Model> { products, categories };

        var explicit_ = new List<RelationshipConfig>
        {
            new()
            {
                DependentTable = "Products",
                DependentColumns = ["CategoryId"],
                PrincipalTable = "Categories",
                PrincipalColumns = ["Id"],
                OnDelete = "Cascade"
            }
        };

        RelationshipMerger.Merge(models, explicit_, "dbo");

        products.ForeignKeys.Count.ShouldBe(1);
        products.ForeignKeys[0].PrincipalTable.ShouldBe("Categories");
        products.ForeignKeys[0].OnDelete.ShouldBe("Cascade");
    }

    [Fact]
    public void Merge_ExplicitOverridesIntrospected()
    {
        var products = MakeModel("Products");
        products.ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_existing",
            Columns = ["CategoryId"],
            PrincipalTable = "OldCategories",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"],
            OnDelete = "NoAction"
        });
        var categories = MakeModel("Categories");
        var models = new List<Model> { products, categories };

        var explicit_ = new List<RelationshipConfig>
        {
            new()
            {
                DependentTable = "Products",
                DependentColumns = ["CategoryId"],
                PrincipalTable = "Categories",
                PrincipalColumns = ["Id"],
                OnDelete = "Cascade"
            }
        };

        RelationshipMerger.Merge(models, explicit_, "dbo");

        products.ForeignKeys.Count.ShouldBe(1);
        products.ForeignKeys[0].PrincipalTable.ShouldBe("Categories");
        products.ForeignKeys[0].OnDelete.ShouldBe("Cascade");
    }

    [Fact]
    public void Merge_PopulatesReferencingForeignKeys()
    {
        var products = MakeModel("Products");
        var categories = MakeModel("Categories");
        products.ForeignKeys.Add(new ForeignKey
        {
            Name = "FK_Products_Categories",
            Columns = ["CategoryId"],
            PrincipalTable = "Categories",
            PrincipalSchema = "dbo",
            PrincipalColumns = ["Id"]
        });

        var models = new List<Model> { products, categories };
        RelationshipMerger.Merge(models, [], "dbo");

        categories.ReferencingForeignKeys.Count.ShouldBe(1);
        categories.ReferencingForeignKeys[0].Table.ShouldBe("Products");
        categories.ReferencingForeignKeys[0].Columns.ShouldContain("CategoryId");
    }

    [Fact]
    public void Merge_SchemaDefaultsApplied()
    {
        var products = MakeModel("Products", "sales");
        var categories = MakeModel("Categories", "dbo");
        var models = new List<Model> { products, categories };

        var explicit_ = new List<RelationshipConfig>
        {
            new()
            {
                DependentTable = "Products",
                DependentSchema = "sales",
                DependentColumns = ["CategoryId"],
                PrincipalTable = "Categories",
                PrincipalSchema = "dbo",
                PrincipalColumns = ["Id"]
            }
        };

        RelationshipMerger.Merge(models, explicit_, "dbo");
        products.ForeignKeys.Count.ShouldBe(1);
    }

    [Fact]
    public void Merge_SelfReferencing()
    {
        var categories = MakeModel("Categories");
        var models = new List<Model> { categories };

        var explicit_ = new List<RelationshipConfig>
        {
            new()
            {
                DependentTable = "Categories",
                DependentColumns = ["ParentId"],
                PrincipalTable = "Categories",
                PrincipalColumns = ["Id"]
            }
        };

        RelationshipMerger.Merge(models, explicit_, "dbo");

        categories.ForeignKeys.Count.ShouldBe(1);
        categories.ForeignKeys[0].PrincipalTable.ShouldBe("Categories");
        categories.ReferencingForeignKeys.Count.ShouldBe(1);
        categories.ReferencingForeignKeys[0].Table.ShouldBe("Categories");
    }

    [Fact]
    public void Merge_SqliteBacked_MergesWithRealFKs()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Categories", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL")
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT, CategoryId INTEGER, FOREIGN KEY (CategoryId) REFERENCES Categories(Id)")
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", new() { Schema = "main" });

        // Add an explicit relationship for a new FK
        var explicit_ = new List<RelationshipConfig>
        {
            new()
            {
                DependentTable = "Products",
                DependentColumns = ["CategoryId"],
                PrincipalTable = "Categories",
                PrincipalColumns = ["Id"],
                OnDelete = "Cascade"
            }
        };

        RelationshipMerger.Merge(models, explicit_, "main");

        var product = models.First(m => m.Name == "Products");
        product.ForeignKeys.Count.ShouldBe(1); // overridden, not duplicated
        product.ForeignKeys[0].OnDelete.ShouldBe("Cascade");

        var category = models.First(m => m.Name == "Categories");
        category.ReferencingForeignKeys.Count.ShouldBe(1);
    }
}

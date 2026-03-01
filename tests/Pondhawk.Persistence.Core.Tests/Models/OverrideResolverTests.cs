using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Models;

public class OverrideResolverTests
{
    private static Model MakeModel(string name, params string[] attrNames)
    {
        var model = new Model { Name = name, Schema = "dbo" };
        foreach (var a in attrNames)
            model.Attributes.Add(new Attribute { Name = a, ClrType = "string", DataType = "nvarchar" });
        return model;
    }

    [Fact]
    public void ExactClassMatch_SetsVariant()
    {
        var models = new List<Model> { MakeModel("Orders") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "Orders", Artifact = "entity", Variant = "SoftDelete" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].GetVariant("entity").ShouldBe("SoftDelete");
    }

    [Fact]
    public void WildcardClassMatch_SetsVariant()
    {
        var models = new List<Model> { MakeModel("Orders"), MakeModel("Products") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Artifact = "entity", Variant = "Auditable" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].GetVariant("entity").ShouldBe("Auditable");
        models[1].GetVariant("entity").ShouldBe("Auditable");
    }

    [Fact]
    public void ExactClassBeatsWildcard()
    {
        var models = new List<Model> { MakeModel("Orders") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Artifact = "entity", Variant = "Generic" },
            new() { Class = "Orders", Artifact = "entity", Variant = "Specific" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].GetVariant("entity").ShouldBe("Specific");
    }

    [Fact]
    public void LastEntryWins_SameSpecificity()
    {
        var models = new List<Model> { MakeModel("Orders") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Artifact = "entity", Variant = "First" },
            new() { Class = "*", Artifact = "entity", Variant = "Second" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].GetVariant("entity").ShouldBe("Second");
    }

    [Fact]
    public void PropertyLevelOverride_ExactClass()
    {
        var models = new List<Model> { MakeModel("Products", "Price") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "Products", Property = "Price", Artifact = "entity", Variant = "Currency" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].Attributes[0].GetVariant("entity").ShouldBe("Currency");
    }

    [Fact]
    public void PropertyLevelOverride_WildcardClass()
    {
        var models = new List<Model> { MakeModel("Products", "CreatedAt"), MakeModel("Orders", "CreatedAt") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Property = "CreatedAt", Artifact = "entity", Variant = "AuditTimestamp" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].Attributes[0].GetVariant("entity").ShouldBe("AuditTimestamp");
        models[1].Attributes[0].GetVariant("entity").ShouldBe("AuditTimestamp");
    }

    [Fact]
    public void PropertyExactClassBeatsWildcard()
    {
        var models = new List<Model> { MakeModel("Orders", "CreatedAt") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Property = "CreatedAt", Artifact = "entity", Variant = "AuditTimestamp" },
            new() { Class = "Orders", Property = "CreatedAt", Artifact = "entity", Variant = "OrderAudit" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].Attributes[0].GetVariant("entity").ShouldBe("OrderAudit");
    }

    [Fact]
    public void Ignore_FiltersProperty_AllArtifacts()
    {
        var models = new List<Model> { MakeModel("Orders", "Id", "RowVersion") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Property = "RowVersion", Ignore = true }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].Attributes.Count.ShouldBe(1);
        models[0].Attributes[0].Name.ShouldBe("Id");
    }

    [Fact]
    public void Ignore_WithArtifact_FiltersOnlyForThatArtifact()
    {
        var models1 = new List<Model> { MakeModel("Orders", "Id", "InternalNotes") };
        var models2 = new List<Model> { MakeModel("Orders", "Id", "InternalNotes") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "Orders", Property = "InternalNotes", Artifact = "dto", Ignore = true }
        };

        OverrideResolver.ApplyOverrides(models1, "dto", overrides, new());
        models1[0].Attributes.Count.ShouldBe(1); // InternalNotes filtered

        OverrideResolver.ApplyOverrides(models2, "entity", overrides, new());
        models2[0].Attributes.Count.ShouldBe(2); // InternalNotes still present
    }

    [Fact]
    public void DataType_AppliesCustomType()
    {
        var models = new List<Model> { MakeModel("Products", "Id") };
        models[0].Attributes[0].ClrType = "int";

        var dataTypes = new Dictionary<string, DataTypeConfig>
        {
            ["Uid"] = new() { ClrType = "string", MaxLength = 28, DefaultValue = "Ulid.NewUlid()" }
        };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Property = "Id", DataType = "Uid" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, dataTypes);
        models[0].Attributes[0].ClrType.ShouldBe("string");
        models[0].Attributes[0].MaxLength.ShouldBe(28);
        models[0].Attributes[0].DefaultValue.ShouldBe("Ulid.NewUlid()");
    }

    [Fact]
    public void DifferentVariantsPerArtifact()
    {
        var models1 = new List<Model> { MakeModel("Products", "Price") };
        var models2 = new List<Model> { MakeModel("Products", "Price") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "Products", Property = "Price", Artifact = "entity", Variant = "Currency" },
            new() { Class = "Products", Property = "Price", Artifact = "dto", Variant = "FormattedCurrency" }
        };

        OverrideResolver.ApplyOverrides(models1, "entity", overrides, new());
        OverrideResolver.ApplyOverrides(models2, "dto", overrides, new());

        models1[0].Attributes[0].GetVariant("entity").ShouldBe("Currency");
        models2[0].Attributes[0].GetVariant("dto").ShouldBe("FormattedCurrency");
    }

    [Fact]
    public void VariantPlusDataType_Combined()
    {
        var models = new List<Model> { MakeModel("Products", "Id") };
        var dataTypes = new Dictionary<string, DataTypeConfig>
        {
            ["Uid"] = new() { ClrType = "string", DefaultValue = "Ulid.NewUlid()" }
        };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "*", Property = "Id", Artifact = "entity", Variant = "UidKey", DataType = "Uid" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, dataTypes);
        models[0].Attributes[0].GetVariant("entity").ShouldBe("UidKey");
        models[0].Attributes[0].ClrType.ShouldBe("string");
        models[0].Attributes[0].DefaultValue.ShouldBe("Ulid.NewUlid()");
    }

    [Fact]
    public void NoMatchingOverride_NoVariant()
    {
        var models = new List<Model> { MakeModel("Products", "Name") };
        var overrides = new List<OverrideConfig>
        {
            new() { Class = "Orders", Artifact = "entity", Variant = "SoftDelete" }
        };

        OverrideResolver.ApplyOverrides(models, "entity", overrides, new());
        models[0].GetVariant("entity").ShouldBe("");
        models[0].Attributes[0].GetVariant("entity").ShouldBe("");
    }
}

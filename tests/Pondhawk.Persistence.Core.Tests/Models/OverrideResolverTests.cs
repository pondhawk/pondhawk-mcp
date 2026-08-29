using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Models;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Models;

public class OverrideResolverTests
{
    private static List<Node> Tree() =>
    [
        new Node
        {
            Name = "Product", Kind = "Class",
            Children =
            [
                new Node { Name = "Id", Kind = "Property" },
                new Node { Name = "Price", Kind = "Property" },
                new Node { Name = "CreatedAt", Kind = "Property" }
            ]
        },
        new Node
        {
            Name = "Order", Kind = "Class",
            Children =
            [
                new Node { Name = "Id", Kind = "Property" },
                new Node { Name = "CreatedAt", Kind = "Property" }
            ]
        }
    ];

    private static Node Child(List<Node> tree, string parent, string child)
        => tree.Single(n => n.Name == parent).Children.Single(c => c.Name == child);

    // --- path matching ---

    [Theory]
    [InlineData("Product", "Product", true)]
    [InlineData("Product", "Order", false)]
    [InlineData("Product/Price", "Product/Price", true)]
    [InlineData("Product/Price", "Order/Price", false)]
    [InlineData("*/CreatedAt", "Product/CreatedAt", true)]
    [InlineData("*/CreatedAt", "Product/Price", false)]
    [InlineData("*/CreatedAt", "CreatedAt", false)]
    [InlineData("Product/*", "Product/Price", true)]
    [InlineData("Product/*", "Product", false)]
    [InlineData("Product", "Product/Price", false)]
    [InlineData("**/CustomerId", "Orders/Submit/CustomerId", true)]
    [InlineData("**/CustomerId", "CustomerId", true)]
    [InlineData("Orders/**", "Orders/Submit/CustomerId", true)]
    [InlineData("Orders/**", "Orders", true)]
    [InlineData("Orders/**", "Product/Submit", false)]
    [InlineData("product/price", "Product/Price", true)]
    public void MatchesPath(string pattern, string path, bool expected)
        => OverrideResolver.MatchesPath(pattern, path).ShouldBe(expected);

    // --- variants ---

    [Fact]
    public void Apply_SetsVariantOnMatchedNode()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity",
            [new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Currency" }]);

        Child(tree, "Product", "Price").GetVariant("entity").ShouldBe("Currency");
        Child(tree, "Product", "Id").GetVariant("entity").ShouldBe("");
    }

    [Fact]
    public void Apply_WildcardMatchesAcrossParents()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity",
            [new OverrideConfig { Path = "*/CreatedAt", Artifact = "entity", Variant = "Audit" }]);

        Child(tree, "Product", "CreatedAt").GetVariant("entity").ShouldBe("Audit");
        Child(tree, "Order", "CreatedAt").GetVariant("entity").ShouldBe("Audit");
    }

    [Fact]
    public void Apply_MoreLiteralSegmentsWins()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity",
        [
            new OverrideConfig { Path = "*/CreatedAt", Artifact = "entity", Variant = "Audit" },
            new OverrideConfig { Path = "Product/CreatedAt", Artifact = "entity", Variant = "Precise" }
        ]);

        Child(tree, "Product", "CreatedAt").GetVariant("entity").ShouldBe("Precise");
        Child(tree, "Order", "CreatedAt").GetVariant("entity").ShouldBe("Audit");
    }

    [Fact]
    public void Apply_SpecificityWinsRegardlessOfOrder()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity",
        [
            new OverrideConfig { Path = "Product/CreatedAt", Artifact = "entity", Variant = "Precise" },
            new OverrideConfig { Path = "*/CreatedAt", Artifact = "entity", Variant = "Audit" }
        ]);

        Child(tree, "Product", "CreatedAt").GetVariant("entity").ShouldBe("Precise");
    }

    [Fact]
    public void Apply_EquallySpecificRules_LaterWins()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity",
        [
            new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "First" },
            new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Second" }
        ]);

        Child(tree, "Product", "Price").GetVariant("entity").ShouldBe("Second");
    }

    [Fact]
    public void Apply_ScopesToArtifact()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "dto",
            [new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Currency" }]);

        Child(tree, "Product", "Price").GetVariant("dto").ShouldBe("");
    }

    [Fact]
    public void Apply_OverrideWithoutArtifact_AppliesToEvery()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "anything",
            [new OverrideConfig { Path = "Product/Price", Variant = "Currency" }]);

        Child(tree, "Product", "Price").GetVariant("anything").ShouldBe("Currency");
    }

    // --- ignore ---

    [Fact]
    public void Apply_IgnoreRemovesMatchedChild()
    {
        var tree = Tree();
        var result = OverrideResolver.Apply(tree, "entity",
            [new OverrideConfig { Path = "*/CreatedAt", Artifact = "entity", Ignore = true }]);

        result.SelectMany(n => n.Children).ShouldNotContain(c => c.Name == "CreatedAt");
        result[0].Children.Count.ShouldBe(2);
    }

    [Fact]
    public void Apply_IgnoreOnRoot_RemovesWholeSubtree()
    {
        var result = OverrideResolver.Apply(Tree(), "entity",
            [new OverrideConfig { Path = "Order", Artifact = "entity", Ignore = true }]);

        result.Select(n => n.Name).ShouldBe(["Product"]);
    }

    // --- metadata merge ---

    [Fact]
    public void Apply_MergesMetadataOntoMatchedNodes()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity",
        [
            new OverrideConfig
            {
                Path = "Product/Price", Artifact = "entity",
                Metadata = new Dictionary<string, object?> { ["Type"] = "decimal", ["Precision"] = 18L }
            }
        ]);

        var price = Child(tree, "Product", "Price");
        price.Metadata["Type"].ShouldBe("decimal");
        price.Metadata["Precision"].ShouldBe(18L);
    }

    [Fact]
    public void Apply_MetadataOverwritesWhatTheModelDeclared()
    {
        var tree = Tree();
        Child(tree, "Product", "Price").Metadata["Type"] = "double";

        OverrideResolver.Apply(tree, "entity",
        [
            new OverrideConfig
            {
                Path = "Product/Price", Artifact = "entity",
                Metadata = new Dictionary<string, object?> { ["Type"] = "decimal" }
            }
        ]);

        Child(tree, "Product", "Price").Metadata["Type"].ShouldBe("decimal");
    }

    [Fact]
    public void Apply_NarrowerMetadataLandsOnTopOfBroader()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity",
        [
            new OverrideConfig
            {
                Path = "Product/Price", Artifact = "entity",
                Metadata = new Dictionary<string, object?> { ["Type"] = "narrow" }
            },
            new OverrideConfig
            {
                Path = "*/*", Artifact = "entity",
                Metadata = new Dictionary<string, object?> { ["Type"] = "broad", ["Access"] = "public" }
            }
        ]);

        var price = Child(tree, "Product", "Price");
        price.Metadata["Type"].ShouldBe("narrow");
        price.Metadata["Access"].ShouldBe("public");
    }

    [Fact]
    public void Apply_NoOverrides_LeavesTreeUntouched()
    {
        var tree = Tree();
        OverrideResolver.Apply(tree, "entity", []).ShouldBe(tree);
    }
}

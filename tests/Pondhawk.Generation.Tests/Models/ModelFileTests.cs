using System.Text.Json;
using Pondhawk.Generation.Models;
using Shouldly;

namespace Pondhawk.Generation.Tests.Models;

public class ModelFileTests
{
    [Fact]
    public void Deserialize_ReadsContractMembers()
    {
        var model = ModelFileLoader.Deserialize("""
            {
              "Name": "Catalog",
              "Nodes": [
                { "Name": "Product", "Kind": "Class",
                  "Children": [ { "Name": "Id", "Kind": "Property" } ] }
              ]
            }
            """);

        model.Name.ShouldBe("Catalog");
        model.Nodes.Count.ShouldBe(1);
        model.Nodes[0].Name.ShouldBe("Product");
        model.Nodes[0].Kind.ShouldBe("Class");
        model.Nodes[0].Children[0].Name.ShouldBe("Id");
    }

    [Fact]
    public void Deserialize_TreatsEverythingElseAsMetadata()
    {
        var model = ModelFileLoader.Deserialize("""
            {
              "Nodes": [
                { "Name": "Price", "Kind": "Property",
                  "Type": "decimal", "IsNullable": false, "Precision": 18 }
              ]
            }
            """);

        var node = model.Nodes[0];
        node.Metadata["Type"].ShouldBe("decimal");
        node.Metadata["IsNullable"].ShouldBe(false);
        node.Metadata["Precision"].ShouldBe(18L);
    }

    [Fact]
    public void Deserialize_ProjectsNestedJsonOntoClrValues()
    {
        // Metadata reaches templates directly, so it must not arrive as JsonElement —
        // that would render into generated files as a raw JSON fragment.
        var model = ModelFileLoader.Deserialize("""
            {
              "Nodes": [
                { "Name": "Product", "Kind": "Class",
                  "Tags": ["a", "b"],
                  "Options": { "Sealed": true } }
              ]
            }
            """);

        var node = model.Nodes[0];
        node.Metadata["Tags"].ShouldBeOfType<List<object?>>().ShouldBe(new List<object?> { "a", "b" });
        var options = node.Metadata["Options"].ShouldBeOfType<Dictionary<string, object?>>();
        options["Sealed"].ShouldBe(true);
    }

    [Fact]
    public void Deserialize_ReadsRootMetadata()
    {
        var model = ModelFileLoader.Deserialize("""
            { "Name": "Catalog", "Version": "2.1", "Nodes": [] }
            """);

        model.Metadata["Version"].ShouldBe("2.1");
        model.GetMember("Version").ShouldBe("2.1");
        model.GetMember("Name").ShouldBe("Catalog");
    }

    [Fact]
    public void Deserialize_NestsToArbitraryDepth()
    {
        var model = ModelFileLoader.Deserialize("""
            {
              "Nodes": [
                { "Name": "Orders", "Kind": "Resource", "Children": [
                  { "Name": "Submit", "Kind": "Operation", "Children": [
                    { "Name": "CustomerId", "Kind": "Parameter" }
                  ]}
                ]}
              ]
            }
            """);

        model.Nodes[0].Children[0].Children[0].Kind.ShouldBe("Parameter");
    }

    [Fact]
    public void Deserialize_MissingKind_Throws()
    {
        var ex = Should.Throw<JsonException>(() => ModelFileLoader.Deserialize("""
            { "Nodes": [ { "Name": "Product" } ] }
            """));

        ex.Message.ShouldContain("Kind");
    }

    [Fact]
    public void Deserialize_MissingName_Throws()
    {
        Should.Throw<JsonException>(() => ModelFileLoader.Deserialize("""
            { "Nodes": [ { "Kind": "Class" } ] }
            """)).Message.ShouldContain("Name");
    }

    [Fact]
    public void Deserialize_MalformedJson_Throws()
    {
        Should.Throw<JsonException>(() => ModelFileLoader.Deserialize("{ not json"));
    }

    [Fact]
    public void Deserialize_AllowsCommentsAndTrailingCommas()
    {
        var model = ModelFileLoader.Deserialize("""
            {
              // the things to generate
              "Nodes": [ { "Name": "Product", "Kind": "Class", } ],
            }
            """);

        model.Nodes.Count.ShouldBe(1);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        const string json = """
            {
              "Name": "Catalog",
              "Nodes": [
                { "Name": "Product", "Kind": "Class", "Note": "a thing", "Children": [
                  { "Name": "Price", "Kind": "Property", "Type": "decimal", "Precision": 18 }
                ]}
              ]
            }
            """;

        var round = ModelFileLoader.Deserialize(ModelFileLoader.Serialize(ModelFileLoader.Deserialize(json)));

        round.Name.ShouldBe("Catalog");
        round.Nodes[0].Metadata["Note"].ShouldBe("a thing");
        round.Nodes[0].Children[0].Metadata["Precision"].ShouldBe(18L);
    }

    [Fact]
    public void Schema_AcceptsAValidModel()
    {
        ModelFileSchema.Validate("""
            { "Name": "Catalog", "Nodes": [ { "Name": "Product", "Kind": "Class", "Type": "x" } ] }
            """).ShouldBeEmpty();
    }

    [Fact]
    public void Schema_RejectsANodeWithoutKind()
    {
        ModelFileSchema.Validate("""
            { "Nodes": [ { "Name": "Product" } ] }
            """).ShouldNotBeEmpty();
    }
}

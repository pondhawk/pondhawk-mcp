using Pondhawk.Generation.Models;
using Shouldly;

namespace Pondhawk.Generation.Tests.Models;

public class ModelDescriberTests
{
    private static ModelDescription Describe(string json) =>
        ModelDescriber.Describe(ModelFileLoader.Deserialize(json), "model.json");

    private const string Catalog = """
        {
          "Name": "Catalog",
          "Nodes": [
            { "Name": "Product", "Kind": "Class", "Children": [
                { "Name": "Id",    "Kind": "Property", "Type": "int" },
                { "Name": "Name",  "Kind": "Property", "Type": "string" },
                { "Name": "Price", "Kind": "Property", "Type": "decimal", "IsNullable": true }
            ] },
            { "Name": "Category", "Kind": "Class", "Children": [
                { "Name": "Title", "Kind": "Property", "Type": "string" }
            ] }
          ]
        }
        """;

    // --- shape ---------------------------------------------------------------

    [Fact]
    public void CountsNodesAndDepth()
    {
        var d = Describe(Catalog);

        d.Name.ShouldBe("Catalog");
        d.RootNodes.ShouldBe(2);
        d.TotalNodes.ShouldBe(6);
        d.MaxDepth.ShouldBe(1);
    }

    [Fact]
    public void ReportsTheKindVocabularyWithExamples()
    {
        var kinds = Describe(Catalog).Kinds;

        var property = kinds.First(k => k.Kind == "Property");
        property.Count.ShouldBe(4);
        property.Depths.ShouldBe([1]);
        property.Examples.ShouldBe(["Id", "Name", "Price"]);

        kinds.ShouldContain(k => k.Kind == "Class" && k.Count == 2 && k.Depths.SequenceEqual(new[] { 0 }));
    }

    [Fact]
    public void ExamplesAreCappedSoALargeModelSummarisesLikeASmallOne()
    {
        var nodes = string.Join(",", Enumerable.Range(0, 200).Select(i => $$"""{ "Name": "N{{i}}", "Kind": "Class" }"""));

        var kind = Describe($$"""{ "Name": "Big", "Nodes": [ {{nodes}} ] }""").Kinds.ShouldHaveSingleItem();

        kind.Count.ShouldBe(200);
        kind.Examples.Count.ShouldBe(3);
    }

    [Fact]
    public void ReportsWhichKindsNestInsideWhich()
    {
        Describe(Catalog).Structure.ShouldBe(["Class > Property"]);
    }

    [Fact]
    public void StructureDistinguishesDeeperNesting()
    {
        var d = Describe("""
            {
              "Name": "Api",
              "Nodes": [
                { "Name": "Orders", "Kind": "Resource", "Children": [
                    { "Name": "List", "Kind": "Operation", "Children": [
                        { "Name": "Page", "Kind": "Parameter" }
                    ] }
                ] }
              ]
            }
            """);

        d.Structure.ShouldBe(["Operation > Parameter", "Resource > Operation"]);
        d.MaxDepth.ShouldBe(2);
    }

    [Fact]
    public void ReportsMetadataCoveragePerKind()
    {
        var property = Describe(Catalog).Metadata["Property"];

        property.ShouldContain(k => k.Key == "Type" && k.Present == "4/4");
        // Coverage is the actionable number: 1 of 4 is either a sparse option or a mistake.
        property.ShouldContain(k => k.Key == "IsNullable" && k.Present == "1/4");
    }

    [Fact]
    public void ReportsValueTypes()
    {
        var property = Describe(Catalog).Metadata["Property"];

        property.First(k => k.Key == "Type").Types.ShouldBe(["string"]);
        property.First(k => k.Key == "IsNullable").Types.ShouldBe(["boolean"]);
    }

    [Fact]
    public void AKindWithNoMetadata_IsOmittedRatherThanListedEmpty()
    {
        Describe(Catalog).Metadata.ShouldNotContainKey("Class");
    }

    [Fact]
    public void AnEmptyModel_DescribesCleanly()
    {
        var d = Describe("""{ "Name": "Empty", "Nodes": [] }""");

        d.TotalNodes.ShouldBe(0);
        d.MaxDepth.ShouldBe(0);
        d.Kinds.ShouldBeEmpty();
        d.Notices.ShouldBeEmpty();
    }

    // --- notices --------------------------------------------------------------

    [Fact]
    public void AConsistentModel_ProducesNoNotices()
    {
        Describe(Catalog).Notices.ShouldBeEmpty();
    }

    [Fact]
    public void NoticesASecondNameForOneConcept()
    {
        // The exact drift the guide warns about: DataType creeping in beside an established Type.
        var d = Describe("""
            {
              "Name": "Catalog",
              "Nodes": [ { "Name": "Product", "Kind": "Class", "Children": [
                { "Name": "A", "Kind": "Property", "Type": "int" },
                { "Name": "B", "Kind": "Property", "Type": "int" },
                { "Name": "C", "Kind": "Property", "Type": "int" },
                { "Name": "D", "Kind": "Property", "Type": "int" },
                { "Name": "E", "Kind": "Property", "DataType": "int" }
              ] } ]
            }
            """);

        d.Notices.ShouldContain(n => n.Contains("'DataType'") && n.Contains("'Type'") && n.Contains("second name"));
    }

    [Fact]
    public void NoticesKindsThatDifferOnlyInCase()
    {
        // AppliesTo matches case-insensitively but dispatch builds the macro name from the
        // literal Kind, so these resolve to different macros. Invisible until one renders an
        // error comment into a generated file.
        var d = Describe("""
            {
              "Name": "X",
              "Nodes": [
                { "Name": "A", "Kind": "Class" },
                { "Name": "B", "Kind": "class" }
              ]
            }
            """);

        var notice = d.Notices.ShouldHaveSingleItem();
        notice.ShouldContain("differ only in case");
        notice.ShouldContain("DefaultClass");
        notice.ShouldContain("Defaultclass");
    }

    [Fact]
    public void NoticesAPluralAndSingularKind()
    {
        var d = Describe("""
            {
              "Name": "X",
              "Nodes": [
                { "Name": "A", "Kind": "Property" },
                { "Name": "B", "Kind": "Properties" }
              ]
            }
            """);

        d.Notices.ShouldContain(n => n.Contains("'Property'") && n.Contains("'Properties'"));
    }

    [Fact]
    public void NoticesAKindThatIsATypoOfAnother()
    {
        var d = Describe("""
            {
              "Name": "X",
              "Nodes": [
                { "Name": "A", "Kind": "Property" },
                { "Name": "B", "Kind": "Propety" }
              ]
            }
            """);

        d.Notices.ShouldContain(n => n.Contains("'Propety'"));
    }

    [Fact]
    public void NoticesOneKeyHoldingTwoValueTypes()
    {
        var d = Describe("""
            {
              "Name": "X",
              "Nodes": [ { "Name": "P", "Kind": "Class", "Children": [
                { "Name": "A", "Kind": "Property", "IsKey": true },
                { "Name": "B", "Kind": "Property", "IsKey": "yes" }
              ] } ]
            }
            """);

        d.Notices.ShouldContain(n => n.Contains("'IsKey'") && n.Contains("boolean") && n.Contains("string"));
    }

    [Fact]
    public void ASparseKeyWithNoRival_IsNotANotice()
    {
        // An optional flag on a few nodes is normal modelling. Only a sparse key that shadows
        // an established one is worth interrupting for.
        var d = Describe("""
            {
              "Name": "X",
              "Nodes": [ { "Name": "P", "Kind": "Class", "Children": [
                { "Name": "A", "Kind": "Property", "Type": "int" },
                { "Name": "B", "Kind": "Property", "Type": "int" },
                { "Name": "C", "Kind": "Property", "Type": "int" },
                { "Name": "D", "Kind": "Property", "Type": "int", "Deprecated": true }
              ] } ]
            }
            """);

        d.Notices.ShouldBeEmpty();
    }

    [Fact]
    public void UnrelatedKindsAreNotFlagged()
    {
        var d = Describe("""
            {
              "Name": "X",
              "Nodes": [
                { "Name": "A", "Kind": "Class" },
                { "Name": "B", "Kind": "Enum" },
                { "Name": "C", "Kind": "Interface" }
              ]
            }
            """);

        d.Notices.ShouldBeEmpty();
    }
}

using Pondhawk.Generation.Models;
using Shouldly;

namespace Pondhawk.Generation.Tests.Models;

public class VariantResolutionTests
{
    [Fact]
    public void GetVariant_ReturnsCorrectVariantPerArtifact()
    {
        var node = new Node { Name = "Orders", Kind = "Class" };
        node.SetVariant("entity", "SoftDelete");
        node.SetVariant("dto", "ReadOnly");

        node.GetVariant("entity").ShouldBe("SoftDelete");
        node.GetVariant("dto").ShouldBe("ReadOnly");
    }

    [Fact]
    public void GetVariant_ReturnsEmptyForUnassigned()
    {
        var node = new Node { Name = "Products", Kind = "Class" };
        node.GetVariant("entity").ShouldBe("");
    }

    [Fact]
    public void GetVariant_UnmatchedArtifact_ReturnsEmpty()
    {
        var node = new Node { Name = "Products", Kind = "Class" };
        node.SetVariant("entity", "SoftDelete");
        node.GetVariant("other").ShouldBe("");
    }

    [Fact]
    public void GetVariant_IsCaseInsensitiveOnArtifactName()
    {
        var node = new Node { Name = "Price", Kind = "Property" };
        node.SetVariant("entity", "Currency");
        node.GetVariant("ENTITY").ShouldBe("Currency");
    }
}

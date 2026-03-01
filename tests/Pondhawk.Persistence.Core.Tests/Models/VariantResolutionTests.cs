using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Models;

public class VariantResolutionTests
{
    [Fact]
    public void Model_GetVariant_ReturnsCorrectVariantPerArtifact()
    {
        var model = new Model { Name = "Orders" };
        model.SetVariant("entity", "SoftDelete");
        model.SetVariant("dto", "ReadOnly");

        model.GetVariant("entity").ShouldBe("SoftDelete");
        model.GetVariant("dto").ShouldBe("ReadOnly");
    }

    [Fact]
    public void Model_GetVariant_ReturnsEmptyForUnassigned()
    {
        var model = new Model { Name = "Products" };
        model.GetVariant("entity").ShouldBe("");
    }

    [Fact]
    public void Model_GetVariant_UnmatchedArtifact_ReturnsEmpty()
    {
        var model = new Model { Name = "Products" };
        model.SetVariant("entity", "SoftDelete");
        model.GetVariant("other").ShouldBe("");
    }

    [Fact]
    public void Attribute_GetVariant_ReturnsCorrectVariantPerArtifact()
    {
        var attr = new Attribute { Name = "Price" };
        attr.SetVariant("entity", "Currency");
        attr.SetVariant("dto", "FormattedCurrency");

        attr.GetVariant("entity").ShouldBe("Currency");
        attr.GetVariant("dto").ShouldBe("FormattedCurrency");
    }

    [Fact]
    public void Attribute_GetVariant_ReturnsEmptyForUnassigned()
    {
        var attr = new Attribute { Name = "Id" };
        attr.GetVariant("entity").ShouldBe("");
    }

    [Fact]
    public void Attribute_GetVariant_UnmatchedArtifact_ReturnsEmpty()
    {
        var attr = new Attribute { Name = "Price" };
        attr.SetVariant("entity", "Currency");
        attr.GetVariant("other").ShouldBe("");
    }
}

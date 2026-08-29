using Pondhawk.Generation.Models;
using Shouldly;

namespace Pondhawk.Generation.Tests.Models;

public class NodeTests
{
    [Fact]
    public void GetMember_ResolvesContractMembers()
    {
        var node = new Node { Name = "Product", Kind = "Class" };
        node.Children.Add(new Node { Name = "Id", Kind = "Property" });

        node.GetMember("Name").ShouldBe("Product");
        node.GetMember("Kind").ShouldBe("Class");
        node.GetMember("Children").ShouldBe(node.Children);
    }

    [Fact]
    public void GetMember_FallsBackToMetadata()
    {
        var node = new Node { Name = "Price", Kind = "Property" };
        node.Metadata["Type"] = "decimal";
        node.Metadata["IsNullable"] = false;

        node.GetMember("Type").ShouldBe("decimal");
        node.GetMember("IsNullable").ShouldBe(false);
    }

    [Fact]
    public void GetMember_UnknownMemberReturnsNull()
    {
        // Metadata is heterogeneous by design, so absence is normal and templates
        // branch on it with {% if %} rather than failing.
        var node = new Node { Name = "Id", Kind = "Property" };
        node.GetMember("NoSuchThing").ShouldBeNull();
    }

    [Fact]
    public void GetMember_ContractMembersCannotBeShadowedByMetadata()
    {
        var node = new Node { Name = "Real", Kind = "Class" };
        node.Metadata["Name"] = "Impostor";

        node.GetMember("Name").ShouldBe("Real");
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var node = new Node { Name = "Product", Kind = "Class" };
        node.Metadata["Note"] = "original";
        node.Children.Add(new Node { Name = "Id", Kind = "Property" });

        var clone = node.Clone();
        clone.Metadata["Note"] = "changed";
        clone.Children[0].Name = "Renamed";
        clone.Children.Add(new Node { Name = "Extra", Kind = "Property" });

        node.Metadata["Note"].ShouldBe("original");
        node.Children.Count.ShouldBe(1);
        node.Children[0].Name.ShouldBe("Id");
    }

    [Fact]
    public void Clone_CarriesVariants()
    {
        var node = new Node { Name = "Price", Kind = "Property" };
        node.SetVariant("entity", "Currency");

        node.Clone().GetVariant("entity").ShouldBe("Currency");
    }

    [Fact]
    public void Descend_YieldsSelfAndDescendantsWithPaths()
    {
        var node = new Node { Name = "Orders", Kind = "Resource" };
        var submit = new Node { Name = "Submit", Kind = "Operation" };
        submit.Children.Add(new Node { Name = "CustomerId", Kind = "Parameter" });
        node.Children.Add(submit);

        var paths = node.Descend().Select(d => d.Path).ToList();

        paths.ShouldBe(["Orders", "Orders/Submit", "Orders/Submit/CustomerId"]);
    }
}

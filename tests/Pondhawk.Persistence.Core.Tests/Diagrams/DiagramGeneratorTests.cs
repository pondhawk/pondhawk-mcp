using Pondhawk.Persistence.Core.Diagrams;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Diagrams;

public class DiagramGeneratorTests
{
    [Fact]
    public void GeneratesValidHtml()
    {
        var model = new Model
        {
            Name = "Users",
            Schema = "public",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "Email", DataType = "varchar(255)" }
            ]
        };

        var html = DiagramGenerator.Generate([model]);

        html.ShouldContain("<!DOCTYPE html>");
        html.ShouldContain("</html>");
        html.ShouldContain("<title>");
    }

    [Fact]
    public void ContainsTableBoxes()
    {
        var model = new Model
        {
            Name = "Products",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "Name", DataType = "varchar(100)" },
                new Attribute { Name = "Price", DataType = "decimal", IsNullable = true }
            ]
        };

        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("Products");
        html.ShouldContain("Id");
        html.ShouldContain("Name");
        html.ShouldContain("Price");
        html.ShouldContain("int");
        html.ShouldContain("varchar(100)");
    }

    [Fact]
    public void ContainsFkRelationshipLines()
    {
        var categories = new Model
        {
            Name = "Categories",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var products = new Model
        {
            Name = "Products",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "CategoryId", DataType = "int" }
            ],
            ForeignKeys =
            [
                new ForeignKey { Columns = ["CategoryId"], PrincipalTable = "Categories", PrincipalColumns = ["Id"] }
            ]
        };

        var html = DiagramGenerator.Generate([categories, products]);
        html.ShouldContain("relationships");
        html.ShouldContain("Products");
        html.ShouldContain("Categories");
    }

    [Fact]
    public void ContainsInteractiveJs()
    {
        var html = DiagramGenerator.Generate([]);
        html.ShouldContain("addEventListener");
        html.ShouldContain("mousedown");
        html.ShouldContain("wheel");
        html.ShouldContain("drawLines");
    }

    [Fact]
    public void SelfContained_NoExternalCdnUrls()
    {
        var model = new Model
        {
            Name = "Test",
            Attributes = [new Attribute { Name = "Id", DataType = "int" }]
        };
        var html = DiagramGenerator.Generate([model]);
        // Should not reference external CDN/JS/CSS — SVG namespace URIs are fine
        html.ShouldNotContain("cdn.");
        html.ShouldNotContain("<script src=");
        html.ShouldNotContain("<link rel=\"stylesheet\" href=");
    }

    [Fact]
    public void DagreBundleIncludesGraphlib()
    {
        var model = new Model
        {
            Name = "Test",
            Attributes = [new Attribute { Name = "Id", DataType = "int" }]
        };
        var html = DiagramGenerator.Generate([model]);
        // The embedded bundle must include graphlib so dagre can find it in the browser
        html.ShouldContain("var graphlib=");
        html.ShouldContain("var dagre=");
    }

    [Fact]
    public void EmptySchema_ProducesValidHtml()
    {
        var html = DiagramGenerator.Generate([]);
        html.ShouldContain("<!DOCTYPE html>");
        html.ShouldContain("</html>");
        // No actual table div elements rendered (CSS class definitions are OK)
        html.ShouldNotContain("id=\"table-");
    }

    [Fact]
    public void EnumBoxes()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "Status", Values = [new() { Name = "Active" }, new() { Name = "Inactive" }] }
        };
        var html = DiagramGenerator.Generate([], enums);
        html.ShouldContain("Status");
        html.ShouldContain("Active");
        html.ShouldContain("Inactive");
        html.ShouldContain("enum-box");
    }

    [Fact]
    public void UniqueColumnIcon()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "Email", DataType = "varchar(255)" }
            ],
            Indexes = [new IndexInfo { Name = "IX_Users_Email", Columns = ["Email"], IsUnique = true }]
        };

        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("unique-icon");
        html.ShouldContain("title=\"Unique\"");
    }

    [Fact]
    public void ViewBoxDistinctFromTable()
    {
        var table = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var view = new Model
        {
            Name = "ActiveUsers",
            IsView = true,
            Attributes = [new Attribute { Name = "Id", DataType = "int" }]
        };

        var html = DiagramGenerator.Generate([table, view]);
        html.ShouldContain("class=\"table-box\"");
        html.ShouldContain("class=\"view-box\"");
        html.ShouldContain("view-header");
        html.ShouldContain("ActiveUsers (view)");
    }

    [Fact]
    public void ContainsDagreLayout()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("dagre.graphlib.Graph");
        html.ShouldContain("dagre.layout");
        html.ShouldContain("rankdir");
    }

    [Fact]
    public void PkAndFkStyling()
    {
        var model = new Model
        {
            Name = "Orders",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "CustomerId", DataType = "int" }
            ],
            ForeignKeys =
            [
                new ForeignKey { Columns = ["CustomerId"], PrincipalTable = "Customers", PrincipalColumns = ["Id"] }
            ]
        };

        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("pk-row");
        html.ShouldContain("fk-row");
    }

    [Fact]
    public void ComputeGroups_ViewsGetViewsGroup()
    {
        var view = new Model { Name = "ActiveUsers", IsView = true };
        var groups = DiagramGenerator.ComputeGroups([view], null);
        groups["table-ActiveUsers"].ShouldBe("Views");
    }

    [Fact]
    public void ComputeGroups_EnumsGetEnumsGroup()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "Status", Values = [new() { Name = "Active" }] }
        };
        var groups = DiagramGenerator.ComputeGroups([], enums);
        groups["enum-Status"].ShouldBe("Enums");
    }

    [Fact]
    public void ComputeGroups_UnconnectedTable()
    {
        var model = new Model
        {
            Name = "Settings",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var groups = DiagramGenerator.ComputeGroups([model], null);
        groups["table-Settings"].ShouldBe("Unconnected");
    }

    [Fact]
    public void ComputeGroups_FkChainWalksToRoot()
    {
        var root = new Model
        {
            Name = "Customers",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var orders = new Model
        {
            Name = "Orders",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "CustomerId", DataType = "int" }
            ],
            ForeignKeys = [new ForeignKey { Columns = ["CustomerId"], PrincipalTable = "Customers", PrincipalColumns = ["Id"] }]
        };
        var lines = new Model
        {
            Name = "OrderLines",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "OrderId", DataType = "int" }
            ],
            ForeignKeys = [new ForeignKey { Columns = ["OrderId"], PrincipalTable = "Orders", PrincipalColumns = ["Id"] }]
        };

        var groups = DiagramGenerator.ComputeGroups([root, orders, lines], null);
        groups["table-Customers"].ShouldBe("Customers");
        groups["table-Orders"].ShouldBe("Customers");
        groups["table-OrderLines"].ShouldBe("Customers");
    }

    [Fact]
    public void SidebarRendered()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("id=\"sidebar\"");
        html.ShouldContain("class=\"sidebar-header\"");
        html.ShouldContain("group-btn");
        html.ShouldContain("data-group=\"__all__\"");
    }

    [Fact]
    public void DataGroupAttribute()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("data-group=\"");
    }

    [Fact]
    public void GroupsJsEmitted()
    {
        var parent = new Model
        {
            Name = "Customers",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var child = new Model
        {
            Name = "Orders",
            Attributes =
            [
                new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true },
                new Attribute { Name = "CustomerId", DataType = "int" }
            ],
            ForeignKeys = [new ForeignKey { Columns = ["CustomerId"], PrincipalTable = "Customers", PrincipalColumns = ["Id"] }]
        };
        var html = DiagramGenerator.Generate([parent, child]);
        html.ShouldContain("const groups = {");
        html.ShouldContain("elGroupMap");
    }

    [Fact]
    public void EnumDataGroupAttribute()
    {
        var enums = new List<SchemaFileEnum>
        {
            new() { Name = "Status", Values = [new() { Name = "Active" }] }
        };
        var html = DiagramGenerator.Generate([], enums);
        html.ShouldContain("data-group=\"Enums\"");
    }

    [Fact]
    public void MutuallyExclusiveGroupSelection()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("selectGroup");
        html.ShouldContain("runLayout");
        html.ShouldContain("zoomToFit");
        html.ShouldContain("activeGroup");
    }

    [Fact]
    public void TitleUsesProjectName()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model], projectName: "my-accounting");
        html.ShouldContain("<title>ER Diagram — my-accounting</title>");
    }

    [Fact]
    public void TitleDefaultsWithoutProjectName()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("<title>ER Diagram</title>");
    }

    [Fact]
    public void TitleBarRendersWithProjectNameDescriptionAndDate()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model], projectName: "my-accounting", description: "Financial system schema");
        html.ShouldContain("id=\"titlebar\"");
        html.ShouldContain("<span class=\"titlebar-name\">my-accounting</span>");
        html.ShouldContain("<span class=\"titlebar-desc\">Financial system schema</span>");
        var now = DateTime.Now;
        html.ShouldContain($"Generated {now:ddd, MMM d, yyyy}");
    }

    [Fact]
    public void TitleBarDefaultsToErDiagramWithoutProjectName()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain("<span class=\"titlebar-name\">ER Diagram</span>");
    }

    [Fact]
    public void TitleBarOmitsDescriptionWhenEmpty()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldNotContain("<span class=\"titlebar-desc\">");
    }

    [Fact]
    public void SidebarHeaderSaysEntities()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        html.ShouldContain(">Entities</div>");
        html.ShouldNotContain(">Groups</div>");
    }

    [Fact]
    public void LayoutDirectionPerGroup()
    {
        var model = new Model
        {
            Name = "Users",
            Attributes = [new Attribute { Name = "Id", DataType = "int", IsPrimaryKey = true }]
        };
        var html = DiagramGenerator.Generate([model]);
        // Grid layout for flat groups, dagre TB for FK-chain groups
        html.ShouldContain("lrGroups");
        html.ShouldContain("gridLayout");
        html.ShouldContain("runLayout('TB')");
    }
}

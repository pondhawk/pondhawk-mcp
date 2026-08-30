using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

public class DescribeModelToolTests : IDisposable
{
    private readonly string _tempDir;

    public DescribeModelToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_describe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        File.WriteAllText(Path.Combine(_tempDir, "templates", "t.liquid"), "x");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void WriteModel(string file, string json) => File.WriteAllText(Path.Combine(_tempDir, file), json);

    private ServerContext Configure(params (string Key, string? Model)[] templates)
    {
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = "output",
            Templates = templates.ToDictionary(
                t => t.Key,
                t => new TemplateConfig
                {
                    Path = "templates/t.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    Model = t.Model
                })
        });

        return new ServerContext(_tempDir);
    }

    private static JsonElement Json(string r) => JsonDocument.Parse(r).RootElement;
    private static List<JsonElement> Models(JsonElement root) => root.GetProperty("Models").EnumerateArray().ToList();

    private const string Entities = """
        { "Name": "Catalog", "Nodes": [
            { "Name": "Product", "Kind": "Class", "Children": [
                { "Name": "Id", "Kind": "Property", "Type": "int" } ] } ] }
        """;

    private const string Api = """
        { "Name": "PublicApi", "Nodes": [
            { "Name": "Orders", "Kind": "Resource", "Children": [
                { "Name": "List", "Kind": "Operation", "Verb": "GET" } ] } ] }
        """;

    [Fact]
    public void DescribesTheDefaultModel()
    {
        WriteModel("model.json", Entities);

        var model = Models(Json(DescribeModelTool.Execute(Configure(("entity", null))))).ShouldHaveSingleItem();

        model.GetProperty("Model").GetString().ShouldBe("model.json");
        model.GetProperty("Name").GetString().ShouldBe("Catalog");
        model.GetProperty("TotalNodes").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void WithNoArgument_DescribesEveryModelTheTemplatesRead()
    {
        // The caller is usually reading a project it has not opened, so it should not have to
        // know the filenames to ask about them.
        WriteModel("model.json", Entities);
        WriteModel("api.model.json", Api);

        var models = Models(Json(DescribeModelTool.Execute(
            Configure(("entity", null), ("api", "api.model.json")))));

        models.Select(m => m.GetProperty("Model").GetString()).ShouldBe(["api.model.json", "model.json"]);
    }

    [Fact]
    public void ANamedModel_IsDescribedAlone()
    {
        WriteModel("model.json", Entities);
        WriteModel("api.model.json", Api);

        var model = Models(Json(DescribeModelTool.Execute(
            Configure(("entity", null), ("api", "api.model.json")), model: "api.model.json"))).ShouldHaveSingleItem();

        model.GetProperty("Name").GetString().ShouldBe("PublicApi");
        model.GetProperty("Structure").EnumerateArray().Select(s => s.GetString()).ShouldBe(["Resource > Operation"]);
    }

    [Fact]
    public void AMissingModel_IsReportedRatherThanThrowing()
    {
        var result = Json(DescribeModelTool.Execute(Configure(("entity", null)), model: "nope.json"));

        result.GetProperty("Models").GetArrayLength().ShouldBe(0);
        result.GetProperty("NotFound").EnumerateArray().Select(n => n.GetString()).ShouldBe(["nope.json"]);
        result.GetProperty("Summary").GetString()!.ShouldContain("No model found");
    }

    [Fact]
    public void TheSummaryLeadsWithWhatIsWorthActingOn()
    {
        WriteModel("model.json", """
            { "Name": "X", "Nodes": [
                { "Name": "A", "Kind": "Class" },
                { "Name": "B", "Kind": "class" } ] }
            """);

        Json(DescribeModelTool.Execute(Configure(("entity", null))))
            .GetProperty("Summary").GetString()!.ShouldContain("1 notices");
    }
}

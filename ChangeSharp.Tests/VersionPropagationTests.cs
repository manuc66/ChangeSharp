using ChangeSharp.VersionPropagation;
using System.Xml.Linq;
using System.Text.Json.Nodes;

namespace ChangeSharp.Tests;

[TestFixture]
public class VersionPropagationTests
{
    private string _tempPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "ChangeSharpTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [Test]
    public void MSBuildHandler_UpdatesVersionElement()
    {
        string csprojPath = Path.Combine(_tempPath, "test.csproj");
        File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");

        var handler = new MSBuildVersionHandler();
        var target = new VersionTargetConfig { Path = "test.csproj" };
        
        handler.UpdateVersion(_tempPath, target, "1.1.0");

        string updated = File.ReadAllText(csprojPath);
        Assert.That(updated, Does.Contain("<Version>1.1.0</Version>"));
    }

    [Test]
    public void MSBuildHandler_UpdatesBothVersionAndVersionPrefix_WhenBothExist()
    {
        string csprojPath = Path.Combine(_tempPath, "test_dual.csproj");
        File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.0.0</Version><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>");

        var handler = new MSBuildVersionHandler();
        var target = new VersionTargetConfig { Path = "test_dual.csproj" };

        handler.UpdateVersion(_tempPath, target, "2.0.0");

        string updated = File.ReadAllText(csprojPath);
        Assert.That(updated, Does.Contain("<Version>2.0.0</Version>"));
        Assert.That(updated, Does.Contain("<VersionPrefix>2.0.0</VersionPrefix>"));
    }

    [Test]
    public void MSBuildHandler_AddsVersionElement_IfMissing()
    {
        string csprojPath = Path.Combine(_tempPath, "test_no_version.csproj");
        File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net7.0</TargetFramework></PropertyGroup></Project>");

        var handler = new MSBuildVersionHandler();
        var target = new VersionTargetConfig { Path = "test_no_version.csproj" };
        
        handler.UpdateVersion(_tempPath, target, "2.0.0");

        string updated = File.ReadAllText(csprojPath);
        Assert.That(updated, Does.Contain("<Version>2.0.0</Version>"));
    }

    [Test]
    public void JsonHandler_UpdatesVersionProperty()
    {
        string jsonPath = Path.Combine(_tempPath, "package.json");
        File.WriteAllText(jsonPath, "{\"name\": \"test\", \"version\": \"1.0.0\"}");

        var handler = new JsonVersionHandler();
        var target = new VersionTargetConfig { Path = "package.json" };
        
        handler.UpdateVersion(_tempPath, target, "1.1.0");

        string updated = File.ReadAllText(jsonPath);
        var node = JsonNode.Parse(updated);
        Assert.That(node!["version"]!.ToString(), Is.EqualTo("1.1.0"));
    }

    [Test]
    public void JsonHandler_MissingIntermediatePath_ReturnsWarning()
    {
        string jsonPath = Path.Combine(_tempPath, "config.json");
        File.WriteAllText(jsonPath, "{\"existing\": {\"version\": \"1.0.0\"}}");

        var handler = new JsonVersionHandler();
        var target = new VersionTargetConfig { Path = "config.json", JsonPath = "nonexistent.deep.version" };

        string? warning = handler.UpdateVersion(_tempPath, target, "2.0.0");

        Assert.That(warning, Is.Not.Null);
        Assert.That(warning, Does.Contain("nonexistent"));

        // The file should still be updated
        string updated = File.ReadAllText(jsonPath);
        var node = JsonNode.Parse(updated);
        Assert.That(node!["nonexistent"]?["deep"]?["version"]?.ToString(), Is.EqualTo("2.0.0"));
    }

    [Test]
    public void JsonHandler_ExistingPath_ReturnsNull()
    {
        string jsonPath = Path.Combine(_tempPath, "config.json");
        File.WriteAllText(jsonPath, "{\"version\": \"1.0.0\"}");

        var handler = new JsonVersionHandler();
        var target = new VersionTargetConfig { Path = "config.json", JsonPath = "version" };

        string? warning = handler.UpdateVersion(_tempPath, target, "2.0.0");

        Assert.That(warning, Is.Null);
    }

    [Test]
    public void JsonHandler_UpdatesNestedProperty()
    {
        string jsonPath = Path.Combine(_tempPath, "manifest.json");
        File.WriteAllText(jsonPath, "{\"meta\": {\"version\": \"1.0.0\"}}");

        var handler = new JsonVersionHandler();
        var target = new VersionTargetConfig { Path = "manifest.json", JsonPath = "$.meta.version" };

        handler.UpdateVersion(_tempPath, target, "1.1.0");

        string updated = File.ReadAllText(jsonPath);
        var node = JsonNode.Parse(updated);
        Assert.That(node!["meta"]!["version"]!.ToString(), Is.EqualTo("1.1.0"));
    }

    [Test]
    public void RegexHandler_NullRegex_ReturnsWarning()
    {
        string txtPath = Path.Combine(_tempPath, "version.txt");
        File.WriteAllText(txtPath, "some content");

        var handler = new RegexVersionHandler();
        var target = new VersionTargetConfig { Path = "version.txt", Type = "regex" };

        string? warning = handler.UpdateVersion(_tempPath, target, "1.1.0");

        Assert.That(warning, Is.Not.Null);
        Assert.That(warning, Does.Contain("no regex pattern"));
    }

    [Test]
    public void JsonHandler_UpdatesCustomProperty()
    {
        string jsonPath = Path.Combine(_tempPath, "config.json");
        File.WriteAllText(jsonPath, "{\"appVersion\": \"1.0.0\"}");

        var handler = new JsonVersionHandler();
        var target = new VersionTargetConfig { Path = "config.json", JsonPath = "appVersion" };
        
        handler.UpdateVersion(_tempPath, target, "1.2.3");

        string updated = File.ReadAllText(jsonPath);
        var node = JsonNode.Parse(updated);
        Assert.That(node!["appVersion"]!.ToString(), Is.EqualTo("1.2.3"));
    }

    [Test]
    public void RegexHandler_UpdatesMatchingText()
    {
        string txtPath = Path.Combine(_tempPath, "version.txt");
        File.WriteAllText(txtPath, "export const VERSION = '1.0.0';");

        var handler = new RegexVersionHandler();
        var target = new VersionTargetConfig { Path = "version.txt", Regex = "(?<=VERSION = ')(.*)(?=')" };
        
        handler.UpdateVersion(_tempPath, target, "1.1.0");

        string updated = File.ReadAllText(txtPath);
        Assert.That(updated, Is.EqualTo("export const VERSION = '1.1.0';"));
    }

    [Test]
    public void RegexHandler_WithReplacement_USesVersionPlaceholder()
    {
        string txtPath = Path.Combine(_tempPath, "app.cs");
        File.WriteAllText(txtPath, "[assembly: AssemblyVersion(\"1.0.0\")]");

        var handler = new RegexVersionHandler();
        var target = new VersionTargetConfig
        {
            Path = "app.cs",
            Regex = "AssemblyVersion\\(\".*\"\\)",
            Replacement = "AssemblyVersion(\"$VERSION\")"
        };

        handler.UpdateVersion(_tempPath, target, "2.0.0");

        string updated = File.ReadAllText(txtPath);
        Assert.That(updated, Is.EqualTo("[assembly: AssemblyVersion(\"2.0.0\")]"));
    }

    [Test]
    public void RegexHandler_PathologicalPattern_TimesOutAndReturnsWarning()
    {
        string txtPath = Path.Combine(_tempPath, "version.txt");
        File.WriteAllText(txtPath, new string('a', 30) + "b");

        var handler = new RegexVersionHandler(TimeSpan.FromMilliseconds(1));
        var target = new VersionTargetConfig { Path = "version.txt", Regex = "(a+)+$" };

        string? warning = handler.UpdateVersion(_tempPath, target, "1.1.0");

        Assert.That(warning, Is.Not.Null);
        Assert.That(warning, Does.Contain("timed out"));
    }
}

using NUnit.Framework;

namespace ChangeSharp.Tests;

public class PublicSurfaceValidatorTests
{
    private static Dictionary<string, string> Files(params (string Name, string Content)[] files) =>
        files.ToDictionary(f => f.Name, f => f.Content, StringComparer.Ordinal);

    [Test]
    public void DeriveImpact_NoBase_ReturnsPatch()
    {
        Assert.That(PublicSurfaceValidator.DeriveImpact(null, Files(("a.txt", "x"))), Is.EqualTo("patch"));
    }

    [Test]
    public void DeriveImpact_Unchanged_ReturnsPatch()
    {
        var baseSurface = Files(("a.txt", "line1\nline2"));
        var current = Files(("a.txt", "line1\nline2"));
        Assert.That(PublicSurfaceValidator.DeriveImpact(baseSurface, current), Is.EqualTo("patch"));
    }

    [Test]
    public void DeriveImpact_AddedLines_ReturnsMinor()
    {
        var baseSurface = Files(("a.txt", "line1"));
        var current = Files(("a.txt", "line1\nline2\nline3"));
        Assert.That(PublicSurfaceValidator.DeriveImpact(baseSurface, current), Is.EqualTo("minor"));
    }

    [Test]
    public void DeriveImpact_RemovedLines_ReturnsMajor()
    {
        var baseSurface = Files(("a.txt", "line1\nline2"));
        var current = Files(("a.txt", "line1"));
        Assert.That(PublicSurfaceValidator.DeriveImpact(baseSurface, current), Is.EqualTo("major"));
    }

    [Test]
    public void DeriveImpact_NewFile_ReturnsMinor()
    {
        var baseSurface = Files(("a.txt", "x"));
        var current = Files(("a.txt", "x"), ("b.txt", "y"));
        Assert.That(PublicSurfaceValidator.DeriveImpact(baseSurface, current), Is.EqualTo("minor"));
    }

    [Test]
    public void DeriveImpact_RemovedFile_ReturnsMajor()
    {
        var baseSurface = Files(("a.txt", "x"), ("b.txt", "y"));
        var current = Files(("a.txt", "x"));
        Assert.That(PublicSurfaceValidator.DeriveImpact(baseSurface, current), Is.EqualTo("major"));
    }

    [Test]
    public void BaselinesMatch_Identical_ReturnsMatch()
    {
        var (match, message) = PublicSurfaceValidator.BaselinesMatch(Files(("a.txt", "x")), Files(("a.txt", "x")));
        Assert.That(match, Is.True);
        Assert.That(message, Is.Null);
    }

    [Test]
    public void BaselinesMatch_TextMismatch_ReturnsError()
    {
        var (match, message) = PublicSurfaceValidator.BaselinesMatch(Files(("a.txt", "x")), Files(("a.txt", "y")));
        Assert.That(match, Is.False);
        Assert.That(message, Does.Contain("a.txt"));
    }

    [Test]
    public void BaselinesMatch_MissingFile_ReturnsError()
    {
        var (match, _) = PublicSurfaceValidator.BaselinesMatch(Files(("a.txt", "x")), Files());
        Assert.That(match, Is.False);
    }

    [Test]
    public void BaselinesMatch_JsonFormattingInsensitive()
    {
        var generated = Files(("t.json", """{"a":[1,2]}"""));
        var committed = Files(("t.json", "{\n  \"a\": [1, 2]\n}"));
        var (match, _) = PublicSurfaceValidator.BaselinesMatch(generated, committed);
        Assert.That(match, Is.True);
    }

    [Test]
    public void BaselinesMatch_JsonSemanticDifference_Fails()
    {
        var generated = Files(("t.json", """{"a":[1,2]}"""));
        var committed = Files(("t.json", """{"a":[1,3]}"""));
        var (match, _) = PublicSurfaceValidator.BaselinesMatch(generated, committed);
        Assert.That(match, Is.False);
    }
}

public class McpToolCatalogTests
{
    [Test]
    public void Tools_ExposeTheFourExpectedTools()
    {
        var names = McpToolCatalog.Tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.That(names, Is.EqualTo(new[] { "get_status", "create_fragment", "validate_fragments", "perform_release" }));
    }

    [Test]
    public void CreateFragment_HasExpectedInputSchema()
    {
        var tool = McpToolCatalog.Tools.Single(t => t!["name"]!.GetValue<string>() == "create_fragment");
        var props = tool!["inputSchema"]!["properties"]!.AsObject();
        Assert.That(props.ContainsKey("message"), Is.True);
        Assert.That(props.ContainsKey("category"), Is.True);
        Assert.That(props.ContainsKey("allowMajor"), Is.True);
        Assert.That(props.ContainsKey("separate"), Is.True);
        Assert.That(props.ContainsKey("fragment"), Is.True);
        Assert.That(props.ContainsKey("changelist"), Is.True);
    }

    [Test]
    public void PerformRelease_HasExpectedInputSchema()
    {
        var tool = McpToolCatalog.Tools.Single(t => t!["name"]!.GetValue<string>() == "perform_release");
        var props = tool!["inputSchema"]!["properties"]!.AsObject();
        Assert.That(props.ContainsKey("dryRun"), Is.True);
        Assert.That(props.ContainsKey("allowMajor"), Is.True);
        Assert.That(props.ContainsKey("apiMinLevel"), Is.True);
    }

    [Test]
    public void ValidateFragments_HasApiMinLevel()
    {
        var tool = McpToolCatalog.Tools.Single(t => t!["name"]!.GetValue<string>() == "validate_fragments");
        var props = tool!["inputSchema"]!["properties"]!.AsObject();
        Assert.That(props.ContainsKey("apiMinLevel"), Is.True);
    }
}
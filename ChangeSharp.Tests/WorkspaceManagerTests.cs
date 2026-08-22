using NUnit.Framework;
using System.IO;
using System.Text;

namespace ChangeSharp.Tests;

public class WorkspaceManagerTests
{
    private string _testDir;

    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ChangeSharpTests", Path.GetRandomFileName());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    private void WriteConfig(string maxImpact)
    {
        string config = $$"""
        {
          "SemverPolicy": {
            "MaxImpact": "{{maxImpact}}",
            "Mappings": {
              "Breaking Changes": "Major",
              "Removed": "Major",
              "Changed": "Minor",
              "Added": "Minor",
              "Deprecated": "Minor",
              "Fixed": "Patch",
              "Security": "Patch"
            }
          }
        }
        """;
        File.WriteAllText(Path.Combine(_testDir, "changesharp.json"), config);
    }

    [Test]
    public void Release_NormalWorkflow_Works()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Added a feature", "Added");

        var (nextVersion, _) = manager.Release(DateTime.Today);

        Assert.That(nextVersion, Is.EqualTo("0.1.0"));
        Assert.That(File.Exists(Path.Combine(_testDir, "CHANGELOG.md")), Is.True);
        
        // Fragments should be deleted
        string unreleasedDir = Path.Combine(_testDir, ".changesharp/unreleased");
        Assert.That(Directory.GetFiles(unreleasedDir, "*.md").Length, Is.EqualTo(0));
        
        // Releasing dir should be empty
        string releasingDir = Path.Combine(_testDir, ".changesharp/releasing");
        Assert.That(Directory.GetFiles(releasingDir, "*.md").Length, Is.EqualTo(0));
    }

    [Test]
    public void Release_InterruptedAfterMove_ResumesCorrectly()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        string fragmentPath = manager.CreateFragment("Added a feature", "Added");
        
        // Simulate interruption after moving to releasing dir
        string releasingDir = Path.Combine(_testDir, ".changesharp/releasing");
        Directory.CreateDirectory(releasingDir);
        string filename = Path.GetFileName(fragmentPath);
        File.Move(fragmentPath, Path.Combine(releasingDir, filename));

        // Act
        var (nextVersion, _) = manager.Release(DateTime.Today);

        // Assert
        Assert.That(nextVersion, Is.EqualTo("0.1.0"));
        Assert.That(File.Exists(Path.Combine(_testDir, "CHANGELOG.md")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(_testDir, "CHANGELOG.md")), Contains.Substring("### Added"));
        Assert.That(File.ReadAllText(Path.Combine(_testDir, "CHANGELOG.md")), Contains.Substring("- Added a feature"));
        
        Assert.That(Directory.GetFiles(releasingDir, "*.md").Length, Is.EqualTo(0));
    }

    [Test]
    public void Release_InterruptedAfterChangelogUpdate_ResumesAndCleansUp()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        string fragmentPath = manager.CreateFragment("Added a feature", "Added");
        
        // Simulate interruption after CHANGELOG update but before fragment deletion
        string releasingDir = Path.Combine(_testDir, ".changesharp/releasing");
        Directory.CreateDirectory(releasingDir);
        string filename = Path.GetFileName(fragmentPath);
        File.Move(fragmentPath, Path.Combine(releasingDir, filename));

        // Pre-update CHANGELOG.md
        var releaseDate = DateTime.Today;
        string changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        string content = File.ReadAllText(changelogPath);
        var changeLog = new ChangeLog(content);
        var updated = changeLog.ReleaseWithVersion(releaseDate, "0.1.0", "### Added\n- Added a feature");
        File.WriteAllText(changelogPath, updated.ToString());

        // Act
        var (nextVersion, _) = manager.Release(releaseDate);

        // Assert
        Assert.That(nextVersion, Is.EqualTo("0.1.0"));
        // Fragments should be deleted now
        Assert.That(Directory.GetFiles(releasingDir, "*.md").Length, Is.EqualTo(0));
    }

    [Test]
    public void Release_InvalidFragment_ThrowsBeforeMovingFiles()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        string unreleasedDir = Path.Combine(_testDir, ".changesharp/unreleased");
        string releasingDir = Path.Combine(_testDir, ".changesharp/releasing");

        // Write an invalid fragment directly (no heading)
        File.WriteAllText(Path.Combine(unreleasedDir, "invalid.md"), "Some text without heading\n");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Release(DateTime.Today));
        Assert.That(ex.Message, Does.Contain("Invalid"));

        // Files should NOT have been moved to releasing/
        Assert.That(File.Exists(Path.Combine(unreleasedDir, "invalid.md")), Is.True);
        if (Directory.Exists(releasingDir))
            Assert.That(Directory.GetFiles(releasingDir, "*.md").Length, Is.EqualTo(0));
    }

    [Test]
    public void Release_ValidFragment_StillWorks()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Feature", "Added");

        var (nextVersion, _) = manager.Release(DateTime.Today);

        Assert.That(nextVersion, Is.EqualTo("0.1.0"));
        string unreleasedDir = Path.Combine(_testDir, ".changesharp/unreleased");
        Assert.That(Directory.GetFiles(unreleasedDir, "*.md").Length, Is.EqualTo(0));
    }

    [Test]
    public void Release_ForcedVersion_RespectedOnResume()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        string fragmentPath = manager.CreateFragment("Added a feature", "Added");

        // Simulate interruption after CHANGELOG update but before cleanup
        string releasingDir = Path.Combine(_testDir, ".changesharp/releasing");
        Directory.CreateDirectory(releasingDir);
        string filename = Path.GetFileName(fragmentPath);
        File.Move(fragmentPath, Path.Combine(releasingDir, filename));

        var releaseDate = DateTime.Today;
        string changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        string content = File.ReadAllText(changelogPath);
        var changeLog = new ChangeLog(content);
        var updated = changeLog.ReleaseWithVersion(releaseDate, "0.1.0", "### Added\n- Added a feature");
        File.WriteAllText(changelogPath, updated.ToString());

        // Act — pass a forced version different from the resumed one
        var (nextVersion, _) = manager.Release(releaseDate, forcedVersion: "99.99.99");

        // Assert — forcedVersion should win over resume detection
        Assert.That(nextVersion, Is.EqualTo("99.99.99"));
    }

    [Test]
    public void Release_Conflict_ThrowsException()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Added a feature", "Added");
        
        // Pre-create version 0.1.0 with DIFFERENT content
        string changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, @"# Changelog
## [0.1.0] - 2026-01-01
### Added
- Different feature
");

        // Act & Assert - force version 0.1.0 which already exists with different content
        Assert.Throws<InvalidOperationException>(() => manager.Release(DateTime.Today, forcedVersion: "0.1.0"));
    }

    [Test]
    public void Release_ChangedTriggersMinor_ByDefault()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Changed a feature", "Changed");

        var (nextVersion, _) = manager.Release(DateTime.Today);

        Assert.That(nextVersion, Is.EqualTo("0.1.0"));
    }

    [Test]
    public void Release_SemverPolicyOverride_Works()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        
        // Override policy in changesharp.json
        string configPath = Path.Combine(_testDir, "changesharp.json");
        string json = File.ReadAllText(configPath);
        var config = System.Text.Json.JsonSerializer.Deserialize<ChangeSharpConfig>(json);
        config.SemverPolicy.Mappings["Changed"] = "Major";
        File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(config));
        
        manager.CreateFragment("Changed a feature", "Changed");

        var (nextVersion, _) = manager.Release(DateTime.Today);

        Assert.That(nextVersion, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void SecurityConfig_AllowAgentRelease_DefaultsToTrue()
    {
        var config = new ChangeSharpConfig();
        Assert.That(config.Security.AllowAgentRelease, Is.True, "MCP agent releases should be allowed by default (env var CHANGESHARP_ALLOW_UNSAFE_RELEASE is the primary gate).");
    }

    [Test]
    public void SecurityConfig_DryRunByDefault_DefaultsToFalse()
    {
        var config = new ChangeSharpConfig();
        Assert.That(config.Security.DryRunByDefault, Is.False);
    }

    [Test]
    public void ShouldDryRunByDefault_ReadsConfig()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        Assert.That(manager.ShouldDryRunByDefault(), Is.False, "Default config must not force dry-run.");

        string configPath = Path.Combine(_testDir, "changesharp.json");
        string json = File.ReadAllText(configPath);
        var config = System.Text.Json.JsonSerializer.Deserialize<ChangeSharpConfig>(json);
        config.Security.DryRunByDefault = true;
        File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(config));

        Assert.That(manager.ShouldDryRunByDefault(), Is.True, "Security.DryRunByDefault=true must be honored.");
    }

    [Test]
    public void Initialize_AutoDiscoversTargets_Works()
    {
        // Setup dummy project files
        File.WriteAllText(Path.Combine(_testDir, "Project1.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(_testDir, "SubDir"));
        File.WriteAllText(Path.Combine(_testDir, "SubDir", "Project2.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_testDir, "package.json"), "{\"version\": \"1.0.0\"}");
        File.WriteAllText(Path.Combine(_testDir, "Directory.Build.props"), "<Project />");
        
        var manager = new WorkspaceManager(_testDir);
        var targets = manager.Initialize();

        Assert.That(targets.Count, Is.EqualTo(4));
        Assert.That(targets.Any(t => t.Path == "Project1.csproj" && t.Type == "msbuild"), Is.True);
        Assert.That(targets.Any(t => t.Path == Path.Combine("SubDir", "Project2.csproj") && t.Type == "msbuild"), Is.True);
        Assert.That(targets.Any(t => t.Path == "package.json" && t.Type == "json"), Is.True);
        Assert.That(targets.Any(t => t.Path == "Directory.Build.props" && t.Type == "msbuild"), Is.True);
    }

    [Test]
    public void Validate_ValidFragment_ReturnsValid()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Feature A", "Added");

        var results = manager.Validate();

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].IsValid, Is.True);
    }

    [Test]
    public void Validate_InvalidFragments_ReturnErrors()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        
        // 1. Empty file
        string unreleasedDir = Path.Combine(_testDir, ".changesharp/unreleased");
        File.WriteAllText(Path.Combine(unreleasedDir, "empty.md"), "");
        
        // 2. Wrong heading level
        File.WriteAllText(Path.Combine(unreleasedDir, "wrong_heading.md"), "## Added\n- Feature");
        
        // 3. No recognized category
        File.WriteAllText(Path.Combine(unreleasedDir, "unknown_cat.md"), "### Unknown\n- Feature");

        var results = manager.Validate().OrderBy(r => r.FilePath).ToList();

        Assert.That(results.Count, Is.EqualTo(3));
        
        // empty.md
        var emptyResult = results.First(r => r.FilePath.EndsWith("empty.md"));
        Assert.That(emptyResult.IsValid, Is.False);
        Assert.That(emptyResult.Errors, Contains.Item("Markdown file is empty."));
        
        // unknown_cat.md
        var unknownResult = results.First(r => r.FilePath.EndsWith("unknown_cat.md"));
        Assert.That(unknownResult.IsValid, Is.False);
        Assert.That(unknownResult.Errors.Any(e => e.Contains("Unrecognized category")), Is.True);
        
        // wrong_heading.md
        var headingResult = results.First(r => r.FilePath.EndsWith("wrong_heading.md"));
        Assert.That(headingResult.IsValid, Is.False);
        Assert.That(headingResult.Errors.Any(e => e.Contains("must start with a level 3 heading")), Is.True);
    }

    [Test]
    public void Release_CustomCategory_Works()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        
        // Add custom category to config
        string configPath = Path.Combine(_testDir, "changesharp.json");
        string json = File.ReadAllText(configPath);
        var config = System.Text.Json.JsonSerializer.Deserialize<ChangeSharpConfig>(json);
        config.SemverPolicy.Mappings["Maintenance"] = "Patch";
        File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(config));
        
        manager.CreateFragment("Refactored some code", "Maintenance");

        var (nextVersion, _) = manager.Release(DateTime.Today);

        Assert.That(nextVersion, Is.EqualTo("0.0.1"));
        Assert.That(File.ReadAllText(Path.Combine(_testDir, "CHANGELOG.md")), Contains.Substring("### Maintenance"));
        Assert.That(File.ReadAllText(Path.Combine(_testDir, "CHANGELOG.md")), Contains.Substring("- Refactored some code"));
    }
    [Test]
    public void Initialize_AdditiveDiscovery_Works()
    {
        var manager = new WorkspaceManager(_testDir);
        
        // 1. Initial setup with one project
        File.WriteAllText(Path.Combine(_testDir, "ProjectA.csproj"), "<Project />");
        var initialTargets = manager.Initialize();
        
        Assert.That(initialTargets.Count, Is.EqualTo(1));
        Assert.That(initialTargets[0].Path, Is.EqualTo("ProjectA.csproj"));
        
        // 2. Add a new project
        File.WriteAllText(Path.Combine(_testDir, "ProjectB.csproj"), "<Project />");
        
        // 3. Re-initialize
        var updatedTargets = manager.Initialize();
        
        Assert.That(updatedTargets.Count, Is.EqualTo(1), "Only new targets should be returned");
        Assert.That(updatedTargets[0].Path, Is.EqualTo("ProjectB.csproj"));
        
        // 4. Verify config file contains both
        string configJson = File.ReadAllText(Path.Combine(_testDir, "changesharp.json"));
        Assert.That(configJson, Contains.Substring("ProjectA.csproj"));
        Assert.That(configJson, Contains.Substring("ProjectB.csproj"));
    }

    [Test]
    public void DiscoverNewTargets_FindsUntrackedProjects()
    {
        var manager = new WorkspaceManager(_testDir);
        
        // 1. Initial setup
        File.WriteAllText(Path.Combine(_testDir, "ProjectA.csproj"), "<Project />");
        manager.Initialize();
        
        // 2. Add a new project but don't re-init yet
        File.WriteAllText(Path.Combine(_testDir, "ProjectB.csproj"), "<Project />");
        
        // 3. Discover
        var newTargets = manager.DiscoverNewTargets();
        
        Assert.That(newTargets.Count, Is.EqualTo(1));
        Assert.That(newTargets[0].Path, Is.EqualTo("ProjectB.csproj"));
    }

    [Test]
    public void CreateFragment_RespectsIncludeBranchName()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        
        // We can't easily mock git process in this environment, 
        // but we can check if it handles the config flag.
        string configPath = Path.Combine(_testDir, "changesharp.json");
        File.WriteAllText(configPath, "{\"FragmentNaming\": {\"IncludeBranchName\": false}}");

        string path = manager.CreateFragment("test message", "Added");
        string filename = Path.GetFileName(path);
        
        // Should not have branch name if disabled (format: timestamp-slug.md)
        // Timestamp is 17 digits (yyyyMMddHHmmssfff)
        Assert.That(filename, Does.Match(@"^\d{17}-test-message\.md$"));
        Assert.That(filename, Does.EndWith("-test-message.md"));
    }

    [Test]
    public void CreateFragment_LongMessage_TruncatesSlug()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        string configPath = Path.Combine(_testDir, "changesharp.json");
        File.WriteAllText(configPath, "{\"FragmentNaming\": {\"IncludeBranchName\": false}}");

        string longMessage = new string('x', 100);
        string path = manager.CreateFragment(longMessage, "Added");
        string filename = Path.GetFileName(path);

        // Slug is truncated to 50 chars
        Assert.That(filename.Length, Is.LessThan(75));
        Assert.That(filename, Does.Match(@"^\d{17}-[a-z]{1,50}\.md$"));
        // The slug part (between timestamp and .md) should be at most 50 chars
        string slug = Path.GetFileNameWithoutExtension(filename).Substring(18); // after timestamp + hyphen
        Assert.That(slug.Length, Is.LessThanOrEqualTo(50));
    }

    [Test]
    public void Initialize_SortsVersionTargets_Alphabetically()
    {
        var manager = new WorkspaceManager(_testDir);
        
        // Create projects in non-alphabetical order
        File.WriteAllText(Path.Combine(_testDir, "Z_Project.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_testDir, "A_Project.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_testDir, "M_Project.csproj"), "<Project />");
        
        manager.Initialize();
        
        string configJson = File.ReadAllText(Path.Combine(_testDir, "changesharp.json"));
        
        // Check order in JSON string
        int indexA = configJson.IndexOf("A_Project.csproj");
        int indexM = configJson.IndexOf("M_Project.csproj");
        int indexZ = configJson.IndexOf("Z_Project.csproj");
        
        Assert.That(indexA, Is.LessThan(indexM));
        Assert.That(indexM, Is.LessThan(indexZ));
    }

    [Test]
    public void CheckApiMinLevel_NoFragments_AlwaysPasses()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        var (pass, impact, name) = manager.CheckApiMinLevel("major");

        Assert.That(pass, Is.True);
        Assert.That(impact, Is.EqualTo(0));
        Assert.That(name, Is.EqualTo("none"));
    }

    [Test]
    public void CheckApiMinLevel_FixedFragment_PassesPatch_FailsMinor()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Fix a bug", "Fixed");

        var (patchPass, _, _) = manager.CheckApiMinLevel("patch");
        Assert.That(patchPass, Is.True);

        var (minorPass, impact, name) = manager.CheckApiMinLevel("minor");
        Assert.That(minorPass, Is.False);
        Assert.That(impact, Is.EqualTo(1));
        Assert.That(name, Is.EqualTo("patch"));

        var (majorPass, _, _) = manager.CheckApiMinLevel("major");
        Assert.That(majorPass, Is.False);
    }

    [Test]
    public void CheckApiMinLevel_AddedFragment_PassesMinor_FailsMajor()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("New feature", "Added");

        var (patchPass, _, _) = manager.CheckApiMinLevel("patch");
        Assert.That(patchPass, Is.True);

        var (minorPass, _, _) = manager.CheckApiMinLevel("minor");
        Assert.That(minorPass, Is.True);

        var (majorPass, impact, name) = manager.CheckApiMinLevel("major");
        Assert.That(majorPass, Is.False);
        Assert.That(impact, Is.EqualTo(2));
        Assert.That(name, Is.EqualTo("minor"));
    }

    [Test]
    public void CheckApiMinLevel_BreakingFragment_PassesMajor()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Breaking API change", "Breaking Changes");

        var (pass, impact, name) = manager.CheckApiMinLevel("major");

        Assert.That(pass, Is.True);
        Assert.That(impact, Is.EqualTo(3));
        Assert.That(name, Is.EqualTo("major"));
    }

    [Test]
    public void CheckApiMinLevel_MixedFragments_UsesHighestImpact()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Fix a bug", "Fixed");        // impact 1
        manager.CreateFragment("Deprecate old API", "Deprecated"); // impact 2

        var (patchPass, _, _) = manager.CheckApiMinLevel("patch");
        Assert.That(patchPass, Is.True);

        var (minorPass, impact, name) = manager.CheckApiMinLevel("minor");
        Assert.That(minorPass, Is.True);
        Assert.That(impact, Is.EqualTo(2));
        Assert.That(name, Is.EqualTo("minor"));

        var (majorPass, _, _) = manager.CheckApiMinLevel("major");
        Assert.That(majorPass, Is.False);
    }

    [Test]
    public void CheckApiMinLevel_InvalidLevel_Throws()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Fix a bug", "Fixed");

        Assert.Throws<ArgumentException>(() => manager.CheckApiMinLevel("foo"));
    }

    [Test]
    public void CheckApiMinLevel_InvalidLevel_ThrowsEvenWithoutFragments()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        Assert.Throws<ArgumentException>(() => manager.CheckApiMinLevel("foo"));
    }

    [Test]
    public void CheckApiMaxLevel_NoFragments_AlwaysPasses()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");

        var (pass, impact, name, maxAllowed, offending) = manager.CheckApiMaxLevel();

        Assert.That(pass, Is.True);
        Assert.That(impact, Is.EqualTo(0));
        Assert.That(name, Is.EqualTo("none"));
        Assert.That(maxAllowed, Is.EqualTo(2));
        Assert.That(offending, Is.Empty);
    }

    [Test]
    public void CheckApiMaxLevel_DefaultMajor_AllowsEverything()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Breaking API change", "Breaking Changes");

        var (pass, impact, name, maxAllowed, offending) = manager.CheckApiMaxLevel();

        Assert.That(pass, Is.True);
        Assert.That(impact, Is.EqualTo(3));
        Assert.That(name, Is.EqualTo("major"));
        Assert.That(maxAllowed, Is.EqualTo(3));
        Assert.That(offending, Is.Empty);
    }

    [Test]
    public void CheckApiMaxLevel_FixedFragment_PassesMinorCap()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");
        manager.CreateFragment("Fix a bug", "Fixed");

        var (pass, _, _, maxAllowed, offending) = manager.CheckApiMaxLevel();

        Assert.That(pass, Is.True);
        Assert.That(maxAllowed, Is.EqualTo(2));
        Assert.That(offending, Is.Empty);
    }

    [Test]
    public void CheckApiMaxLevel_BreakingFragment_FailsMinorCap()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");
        manager.CreateFragment("Breaking API change", "Breaking Changes");

        var (pass, impact, name, maxAllowed, offending) = manager.CheckApiMaxLevel();

        Assert.That(pass, Is.False);
        Assert.That(impact, Is.EqualTo(3));
        Assert.That(name, Is.EqualTo("major"));
        Assert.That(maxAllowed, Is.EqualTo(2));
        Assert.That(offending, Is.EqualTo(new[] { "Breaking Changes" }));
    }

    [Test]
    public void CheckApiMaxLevel_MixedFragments_ReportsOnlyOffendingCategories()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");
        manager.CreateFragment("Fix a bug", "Fixed");
        manager.CreateFragment("New feature", "Added");
        manager.CreateFragment("Breaking API change", "Breaking Changes");

        var (pass, _, _, _, offending) = manager.CheckApiMaxLevel();

        Assert.That(pass, Is.False);
        Assert.That(offending, Is.EqualTo(new[] { "Breaking Changes" }));
    }

    [Test]
    public void CheckApiMaxLevel_InvalidMaxImpact_Throws()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("galactic");

        Assert.Throws<ArgumentException>(() => manager.CheckApiMaxLevel());
    }

    [Test]
    public void IsCategoryWithinMaxImpact_DefaultMajor_AllowsMajorCategories()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        var (allowed, impact, maxAllowed) = manager.IsCategoryWithinMaxImpact("Breaking Changes");

        Assert.That(allowed, Is.True);
        Assert.That(impact, Is.EqualTo(3));
        Assert.That(maxAllowed, Is.EqualTo(3));
    }

    [Test]
    public void IsCategoryWithinMaxImpact_MinorCap_BlocksMajorCategories()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");

        var (breakingAllowed, breakingImpact, maxAllowed) = manager.IsCategoryWithinMaxImpact("Breaking Changes");
        Assert.That(breakingAllowed, Is.False);
        Assert.That(breakingImpact, Is.EqualTo(3));
        Assert.That(maxAllowed, Is.EqualTo(2));

        var (removedAllowed, _, _) = manager.IsCategoryWithinMaxImpact("Removed");
        Assert.That(removedAllowed, Is.False);

        var (addedAllowed, _, _) = manager.IsCategoryWithinMaxImpact("Added");
        Assert.That(addedAllowed, Is.True);
    }

    [Test]
    public void IsCategoryWithinMaxImpact_UnknownCategory_Allowed()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");

        var (allowed, impact, maxAllowed) = manager.IsCategoryWithinMaxImpact("Nonsense");

        Assert.That(allowed, Is.True);
        Assert.That(impact, Is.EqualTo(0));
        Assert.That(maxAllowed, Is.EqualTo(2));
    }

    [Test]
    public void IsCategoryWithinMaxImpact_InvalidMaxImpact_Throws()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("galactic");

        Assert.Throws<ArgumentException>(() => manager.IsCategoryWithinMaxImpact("Added"));
    }

    [Test]
    public void GetCreateFragmentError_BlockedCategory_ReturnsMessage()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");

        string? error = manager.GetCreateFragmentError("Breaking Changes", allowMajor: false);

        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("major"));
        Assert.That(error, Does.Contain("--allow-major"));
    }

    [Test]
    public void GetCreateFragmentError_AllowMajor_ReturnsNull()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");

        string? error = manager.GetCreateFragmentError("Breaking Changes", allowMajor: true);

        Assert.That(error, Is.Null);
    }

    [Test]
    public void GetCreateFragmentError_WithinCap_ReturnsNull()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");

        string? error = manager.GetCreateFragmentError("Added", allowMajor: false);

        Assert.That(error, Is.Null);
    }

    [Test]
    public void GetReleaseGateResult_CapExceededWithoutAllowMajor_Blocks()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");
        manager.CreateFragment("Breaking API change", "Breaking Changes");

        var (blocked, message, capExceeded) = manager.GetReleaseGateResult(null, allowMajor: false);

        Assert.That(blocked, Is.True);
        Assert.That(capExceeded, Is.True);
        Assert.That(message, Does.Contain("major"));
        Assert.That(message, Does.Contain("--allow-major"));
    }

    [Test]
    public void GetReleaseGateResult_AllowMajor_ProceedsButFlagsCapExceeded()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");
        manager.CreateFragment("Breaking API change", "Breaking Changes");

        var (blocked, _, capExceeded) = manager.GetReleaseGateResult(null, allowMajor: true);

        Assert.That(blocked, Is.False);
        Assert.That(capExceeded, Is.True);
    }

    [Test]
    public void GetReleaseGateResult_FloorBelowRequired_Blocks()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Fix a bug", "Fixed");

        var (blocked, message, capExceeded) = manager.GetReleaseGateResult("minor", allowMajor: false);

        Assert.That(blocked, Is.True);
        Assert.That(capExceeded, Is.False);
        Assert.That(message, Does.Contain("API surface requires"));
    }

    [Test]
    public void GetReleaseGateResult_AllGood_ReturnsNotBlocked()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        WriteConfig("minor");
        manager.CreateFragment("New feature", "Added");

        var (blocked, _, capExceeded) = manager.GetReleaseGateResult(null, allowMajor: false);

        Assert.That(blocked, Is.False);
        Assert.That(capExceeded, Is.False);
    }

    [Test]
    public void ListFragmentFiles_EmptyDir_ReturnsEmpty()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        var files = manager.ListFragmentFiles();

        Assert.That(files, Is.Empty);
    }

    [Test]
    public void ListFragmentFiles_WithFragments_ReturnsRelativePaths()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Feature A", "Added");
        manager.CreateFragment("Fix B", "Fixed");

        var files = manager.ListFragmentFiles();

        Assert.That(files.Length, Is.EqualTo(2));
        Assert.That(files.All(f => f.StartsWith(".changesharp/unreleased/")), Is.True);
    }

    [Test]
    public void RemoveFragment_RemovesByRelativePath()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        string path = manager.CreateFragment("Feature A", "Added");

        string relativePath = Path.GetRelativePath(_testDir, path);
        bool removed = manager.RemoveFragment(relativePath);

        Assert.That(removed, Is.True);
        Assert.That(File.Exists(path), Is.False);
        Assert.That(manager.ListFragmentFiles(), Is.Empty);
    }

    [Test]
    public void RemoveFragment_MissingFile_ReturnsFalse()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        bool removed = manager.RemoveFragment(".changesharp/unreleased/nonexistent.md");

        Assert.That(removed, Is.False);
    }

    [Test]
    public void RemoveAllFragments_RemovesAll()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Feature A", "Added");
        manager.CreateFragment("Feature B", "Added");
        manager.CreateFragment("Fix C", "Fixed");

        int count = manager.RemoveAllFragments();

        Assert.That(count, Is.EqualTo(3));
        Assert.That(manager.ListFragmentFiles(), Is.Empty);
    }

    [Test]
    public void RemoveAllFragments_EmptyDir_ReturnsZero()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        int count = manager.RemoveAllFragments();

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void GetStatus_NoFragments_NextVersionEqualsCurrent()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();

        manager.GetStatus(out int count, out _, out string current, out string next);

        Assert.That(count, Is.EqualTo(0));
        Assert.That(next, Is.EqualTo(current));
    }

    [Test]
    public void GetStatus_AfterRelease_NextVersionEqualsCurrent()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Added a feature", "Added");
        manager.Release(DateTime.Today);

        manager.GetStatus(out int count, out _, out string current, out string next);

        Assert.That(count, Is.EqualTo(0));
        Assert.That(current, Is.EqualTo("0.1.0"));
        Assert.That(next, Is.EqualTo("0.1.0"));
    }

    [Test]
    public void GetLatestRelease_AfterRelease_ReturnsVersionAndSegment()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Added a feature", "Added");
        manager.CreateFragment("Fixed a bug", "Fixed");
        manager.Release(DateTime.Today);

        var (version, body) = manager.GetLatestRelease();

        Assert.That(version, Is.EqualTo("0.1.0"));
        Assert.That(body, Does.Contain("### Added"));
        Assert.That(body, Does.Contain("- Added a feature"));
        Assert.That(body, Does.Contain("- Fixed a bug"));
    }

    [Test]
    public void GetLatestRelease_NoChangelog_Throws()
    {
        var manager = new WorkspaceManager(_testDir);

        var ex = Assert.Throws<InvalidOperationException>(() => manager.GetLatestRelease());
        Assert.That(ex.Message, Does.Contain("Changelog not found"));
    }

    [Test]
    public void GetLatestRelease_OnlyUnreleased_Throws()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Added a feature", "Added");

        var ex = Assert.Throws<InvalidOperationException>(() => manager.GetLatestRelease());
        Assert.That(ex.Message, Does.Contain("No released version"));
    }

    [Test]
    public void GetVersionRelease_SpecificVersion_ReturnsSegment()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("First feature", "Added");
        manager.Release(DateTime.Today);
        manager.CreateFragment("Second feature", "Added");
        manager.Release(DateTime.Today);

        var (version, body) = manager.GetVersionRelease("0.1.0");

        Assert.That(version, Is.EqualTo("0.1.0"));
        Assert.That(body, Does.Contain("- First feature"));
        Assert.That(body, Does.Not.Contain("- Second feature"));
    }

    [Test]
    public void GetVersionRelease_UnknownVersion_Throws()
    {
        var manager = new WorkspaceManager(_testDir);
        manager.Initialize();
        manager.CreateFragment("Added a feature", "Added");
        manager.Release(DateTime.Today);

        var ex = Assert.Throws<InvalidOperationException>(() => manager.GetVersionRelease("9.9.9"));
        Assert.That(ex.Message, Does.Contain("not found in the changelog"));
    }
}

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChangeSharp.VersionPropagation;

namespace ChangeSharp;

public class WorkspaceManager
{
    private readonly string _basePath;
    private const string ConfigFileName = "changesharp.json";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);
    private static readonly string GitExecutable = ResolveGitExecutable();

    private static string ResolveGitExecutable()
    {
        string fileName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (pathValue != null)
        {
            foreach (string dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return fileName;
    }

    public WorkspaceManager(string? basePath = null)
    {
        _basePath = basePath ?? Directory.GetCurrentDirectory();
    }

    private string ConfigFilePath => Path.Combine(_basePath, ConfigFileName);

    public List<VersionTargetConfig> Initialize()
    {
        // 1. Create default configuration
        bool isNewConfig = !File.Exists(ConfigFilePath);
        var config = isNewConfig ? new ChangeSharpConfig() : LoadConfig();

        // Auto-discovery of version targets
        var discoveredTargets = DiscoverVersionTargets();
        var newTargets = new List<VersionTargetConfig>();

        if (isNewConfig)
        {
            config.VersionTargets.AddRange(discoveredTargets);
            newTargets.AddRange(discoveredTargets);
        }
        else
        {
            var existingPaths = new HashSet<string>(config.VersionTargets.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var discovered in discoveredTargets)
            {
                if (!existingPaths.Contains(discovered.Path))
                {
                    config.VersionTargets.Add(discovered);
                    newTargets.Add(discovered);
                }
            }
        }

        if (isNewConfig || newTargets.Any())
        {
            // Sort targets by path to ensure deterministic JSON and minimize merge conflicts
            config.VersionTargets = config.VersionTargets
                .OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigFilePath, json, Encoding.UTF8);
        }

        // 2. Create unreleased directory
        string unreleasedPath = Path.Combine(_basePath, config.UnreleasedDir);
        if (!Directory.Exists(unreleasedPath))
        {
            Directory.CreateDirectory(unreleasedPath);
        }

        // 2b. Create prereleases directory
        string prereleasesPath = Path.Combine(_basePath, config.PrereleasesDir);
        if (!Directory.Exists(prereleasesPath))
        {
            Directory.CreateDirectory(prereleasesPath);
        }

        // 2c. Create releasing directory
        string releasingPath = Path.Combine(_basePath, config.ReleasingDir);
        if (!Directory.Exists(releasingPath))
        {
            Directory.CreateDirectory(releasingPath);
        }

        // 3. Create default CHANGELOG.md if it doesn't exist
        string changelogPath = Path.Combine(_basePath, config.ChangelogPath);
        if (!File.Exists(changelogPath))
        {
            string defaultChangelog = @"# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
";
            File.WriteAllText(changelogPath, defaultChangelog, Encoding.UTF8);
        }

        return newTargets;
    }

    public List<VersionTargetConfig> DiscoverNewTargets()
    {
        if (!File.Exists(ConfigFilePath)) return DiscoverVersionTargets();

        var config = LoadConfig();
        var allDiscovered = DiscoverVersionTargets();

        var existingPaths = new HashSet<string>(config.VersionTargets.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);

        return allDiscovered.Where(t => !existingPaths.Contains(t.Path)).ToList();
    }

    public List<VersionTargetConfig> DiscoverVersionTargets()
    {
        var targets = new List<VersionTargetConfig>();
        var rootDir = new DirectoryInfo(_basePath);

        // Patterns to look for
        var msbuildPatterns = new[] { "*.csproj", "Directory.Build.props" };
        var jsonPatterns = new[] { "package.json" };

        // Directories to skip
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "node_modules", ".changesharp" };

        DiscoverRecursive(rootDir, targets, msbuildPatterns, jsonPatterns, skipDirs, 0, 3); // max depth 3

        return targets;
    }

    private void DiscoverRecursive(DirectoryInfo dir, List<VersionTargetConfig> targets, string[] msbuildPatterns, string[] jsonPatterns, HashSet<string> skipDirs, int currentDepth, int maxDepth)
    {
        if (currentDepth > maxDepth) return;
        if (skipDirs.Contains(dir.Name)) return;

        foreach (var pattern in msbuildPatterns)
        {
            foreach (var file in dir.GetFiles(pattern))
            {
                targets.Add(new VersionTargetConfig
                {
                    Path = Path.GetRelativePath(_basePath, file.FullName),
                    Type = "msbuild"
                });
            }
        }

        foreach (var pattern in jsonPatterns)
        {
            foreach (var file in dir.GetFiles(pattern))
            {
                targets.Add(new VersionTargetConfig
                {
                    Path = Path.GetRelativePath(_basePath, file.FullName),
                    Type = "json",
                    JsonPath = "version"
                });
            }
        }

        foreach (var subDir in dir.GetDirectories())
        {
            DiscoverRecursive(subDir, targets, msbuildPatterns, jsonPatterns, skipDirs, currentDepth + 1, maxDepth);
        }
    }

    public List<ValidationResult> Validate()
    {
        var config = LoadConfig();
        string unreleasedPath = Path.Combine(_basePath, config.UnreleasedDir);
        if (!Directory.Exists(unreleasedPath)) return new List<ValidationResult>();

        var files = Directory.GetFiles(unreleasedPath, "*.md");
        var results = new List<ValidationResult>();
        var parser = new ChangelogParser();

        foreach (var file in files)
        {
            var result = new ValidationResult { FilePath = Path.GetRelativePath(_basePath, file) };
            string content = File.ReadAllText(file, Encoding.UTF8);

            // Simple validation using the parser
            var changeSet = parser.Parse(content);

            if (changeSet.IsEmpty())
            {
                result.Errors.Add("Fragment is empty.");
            }

            // Flag each unrecognized section individually
            var validCategories = new HashSet<string>(config.SemverPolicy.Mappings.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var section in changeSet.Sections)
            {
                if (section.Value.Count > 0 && !validCategories.Contains(section.Key))
                {
                    string validCats = string.Join(", ", config.SemverPolicy.Mappings.Keys);
                    result.Errors.Add($"Unrecognized category '{section.Key}'. Recognized: {validCats}");
                }
            }

            // More specific validation using Markdig directly
            var doc = Markdig.Markdown.Parse(content);
            if (doc.Count == 0)
            {
                result.Errors.Add("Markdown file is empty.");
            }
            else
            {
                var firstBlock = doc.FirstOrDefault();
                if (firstBlock is not Markdig.Syntax.HeadingBlock hb || hb.Level != 3)
                {
                    result.Errors.Add("Fragment must start with a level 3 heading (### Category).");
                }
            }

            result.IsValid = result.Errors.Count == 0;
            results.Add(result);
        }

        return results;
    }

    public (bool Pass, int MaxImpact, string MaxLevelName) CheckApiMinLevel(string minLevel)
    {
        int required = NextVersionComputer.ParseImpact(minLevel);
        if (required == 0)
            throw new ArgumentException($"Invalid API level '{minLevel}'. Valid values: patch, minor, major.", nameof(minLevel));

        var config = LoadConfig();
        GetStatus(out int count, out ChangeSet merged, out _, out _);

        // No fragments → nothing to check, always pass
        if (count == 0)
            return (true, 0, "none");

        var (maxImpact, maxLevelName) = ComputeMaxImpact(merged, config.SemverPolicy);
        return (maxImpact >= required, maxImpact, maxLevelName);
    }

    public (bool Pass, int MaxImpact, string MaxLevelName, int MaxAllowed, string[] OffendingCategories) CheckApiMaxLevel()
    {
        var config = LoadConfig();
        int maxAllowed = NextVersionComputer.ParseImpact(config.SemverPolicy.MaxImpact);
        if (maxAllowed == 0)
            throw new ArgumentException($"Invalid SemverPolicy.MaxImpact '{config.SemverPolicy.MaxImpact}'. Valid values: patch, minor, major.", "MaxImpact");

        GetStatus(out int count, out ChangeSet merged, out _, out _);

        // No fragments → nothing to check, always pass
        if (count == 0)
            return (true, 0, "none", maxAllowed, Array.Empty<string>());

        var (maxImpact, maxLevelName) = ComputeMaxImpact(merged, config.SemverPolicy);

        var offendingCategories = merged.Sections
            .Where(pair => pair.Value.Count > 0
                && config.SemverPolicy.Mappings.TryGetValue(pair.Key, out var impact)
                && NextVersionComputer.ParseImpact(impact) > maxAllowed)
            .Select(pair => pair.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (maxImpact <= maxAllowed, maxImpact, maxLevelName, maxAllowed, offendingCategories);
    }

    public (bool Allowed, int CategoryImpact, int MaxAllowed) IsCategoryWithinMaxImpact(string category)
    {
        var config = LoadConfig();
        int maxAllowed = NextVersionComputer.ParseImpact(config.SemverPolicy.MaxImpact);
        if (maxAllowed == 0)
            throw new ArgumentException($"Invalid SemverPolicy.MaxImpact '{config.SemverPolicy.MaxImpact}'. Valid values: patch, minor, major.", "MaxImpact");

        if (!config.SemverPolicy.Mappings.TryGetValue(category, out var impact))
            return (true, 0, maxAllowed);

        int categoryImpact = NextVersionComputer.ParseImpact(impact);
        return (categoryImpact <= maxAllowed, categoryImpact, maxAllowed);
    }

    public string? GetCreateFragmentError(string category, bool allowMajor)
    {
        if (allowMajor) return null;

        var (allowed, categoryImpact, maxAllowed) = IsCategoryWithinMaxImpact(category);
        if (allowed) return null;

        return $"Category '{category}' requires a {NextVersionComputer.ImpactName(categoryImpact)} bump, above the configured SemverPolicy.MaxImpact ({NextVersionComputer.ImpactName(maxAllowed)}). Use --allow-major to override.";
    }

    public (bool Blocked, string Message, bool CapExceeded) GetReleaseGateResult(string? apiMinLevel, bool allowMajor)
    {
        if (apiMinLevel != null)
        {
            var (pass, maxImpact, maxLevelName) = CheckApiMinLevel(apiMinLevel);
            if (!pass)
                return (true, $"API surface requires at least a '{apiMinLevel}' bump, but fragments only reach '{maxLevelName}' (level {maxImpact}).", false);
        }

        var (withinCap, maxImpact2, maxLevelName2, maxAllowed, offendingCategories) = CheckApiMaxLevel();
        bool capExceeded = !withinCap;
        if (capExceeded && !allowMajor)
        {
            string categories = offendingCategories.Length > 0 ? $" ({string.Join(", ", offendingCategories)})" : "";
            string message = $"Release would bump to '{maxLevelName2}' (level {maxImpact2}), above the configured SemverPolicy.MaxImpact ({NextVersionComputer.ImpactName(maxAllowed)}){categories}. Use --allow-major to proceed.";
            return (true, message, true);
        }

        return (false, "", capExceeded);
    }

    private static (int MaxImpact, string MaxLevelName) ComputeMaxImpact(ChangeSet merged, SemverPolicyConfig policy)
    {
        int maxImpact = 0;
        string maxLevelName = "none";

        foreach (var pair in merged.Sections)
        {
            if (pair.Value.Count > 0 && policy.Mappings.TryGetValue(pair.Key, out var impact))
            {
                int impactValue = NextVersionComputer.ParseImpact(impact);
                if (impactValue > maxImpact)
                {
                    maxImpact = impactValue;
                    maxLevelName = impact.ToLowerInvariant();
                }
            }
        }

        return (maxImpact, maxLevelName);
    }

    public string[] ListFragmentFiles()
    {
        var config = LoadConfig();
        string unreleasedPath = Path.Combine(_basePath, config.UnreleasedDir);
        if (!Directory.Exists(unreleasedPath)) return [];

        return Directory.GetFiles(unreleasedPath, "*.md")
            .Select(f => Path.GetRelativePath(_basePath, f))
            .ToArray();
    }

    public bool RemoveFragment(string relativePath)
    {
        string fullPath = Path.Combine(_basePath, relativePath);
        if (!File.Exists(fullPath)) return false;

        File.Delete(fullPath);
        return true;
    }

    public int RemoveAllFragments()
    {
        var config = LoadConfig();
        string unreleasedPath = Path.Combine(_basePath, config.UnreleasedDir);
        if (!Directory.Exists(unreleasedPath)) return 0;

        int count = 0;
        foreach (var file in Directory.GetFiles(unreleasedPath, "*.md"))
        {
            File.Delete(file);
            count++;
        }
        return count;
    }

    public string CreateFragment(string message, string category)
    {
        var config = LoadConfig();
        string unreleasedPath = Path.Combine(_basePath, config.UnreleasedDir);
        if (!Directory.Exists(unreleasedPath))
        {
            Directory.CreateDirectory(unreleasedPath);
        }

        // Clean/slugify message for filename
        string slug = Slugify(message);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        
        string filename;
        if (config.FragmentNaming.IncludeBranchName)
        {
            string branch = GetGitBranchName() ?? "";
            string branchSlug = string.IsNullOrWhiteSpace(branch) ? "" : Slugify(branch) + "-";
            filename = $"{timestamp}-{branchSlug}{slug}.md";
        }
        else
        {
            filename = $"{timestamp}-{slug}.md";
        }

        string filePath = Path.Combine(unreleasedPath, filename);

        // Normalize category naming (e.g. "Breaking Changes" vs "Added")
        string formattedCategory = NormalizeCategory(category);

        string content = $"### {formattedCategory}{Environment.NewLine}- {message}{Environment.NewLine}";
        File.WriteAllText(filePath, content, Encoding.UTF8);

        return filePath;
    }

    private string? GetGitBranchName()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = GitExecutable,
                Arguments = "rev-parse --abbrev-ref HEAD",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _basePath
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return null;
            
            string branch = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            return process.ExitCode == 0 ? branch : null;
        }
        catch
        {
            return null;
        }
    }

    public void GetStatus(
        out int fragmentCount,
        out ChangeSet mergedChangeSet,
        out string currentVersion,
        out string nextVersion)
    {
        var config = LoadConfig();
        string unreleasedPath = Path.Combine(_basePath, config.UnreleasedDir);
        string releasingPath = Path.Combine(_basePath, config.ReleasingDir);
        
        var unreleasedFragments = Directory.Exists(unreleasedPath)
            ? Directory.GetFiles(unreleasedPath, "*.md")
            : Array.Empty<string>();

        var releasingFragments = Directory.Exists(releasingPath)
            ? Directory.GetFiles(releasingPath, "*.md")
            : Array.Empty<string>();

        var allFragments = unreleasedFragments.Concat(releasingFragments).ToArray();
        fragmentCount = allFragments.Length;

        var parser = new ChangelogParser();
        var changeSets = new List<ChangeSet>();

        foreach (var file in allFragments)
        {
            string fileContent = File.ReadAllText(file, Encoding.UTF8);
            changeSets.Add(parser.Parse(fileContent));
        }

        mergedChangeSet = changeSets.Count > 0
            ? changeSets.Aggregate(new ChangeSet(), (a, b) => a.Merge(b))
            : new ChangeSet();

        currentVersion = GetCurrentVersion(config);
        nextVersion = NextVersionComputer.ComputeVersion(currentVersion, mergedChangeSet, config.SemverPolicy);
    }

    public bool ShouldDryRunByDefault()
    {
        var config = LoadConfig();
        return config.Security.DryRunByDefault;
    }

    public (string Version, string[] Warnings) Release(DateTime releaseDate, bool dryRun = false, string? forcedVersion = null)
    {
        var config = LoadConfig();
        string unreleasedPath = Path.Combine(_basePath, config.UnreleasedDir);
        string releasingPath = Path.Combine(_basePath, config.ReleasingDir);
        string changelogPath = Path.Combine(_basePath, config.ChangelogPath);
        var warnings = new List<string>();

        if (dryRun)
        {
            GetStatus(out int count, out ChangeSet merged, out string current, out string next);
            if (forcedVersion != null) next = forcedVersion;
            if (count == 0) throw new InvalidOperationException("No unreleased fragments found to release.");
            return (next, []);
        }

        // 1. Transactional check & move
        string[] fragments;
        
        if (Directory.Exists(releasingPath) && Directory.GetFiles(releasingPath, "*.md").Length > 0)
        {
            fragments = Directory.GetFiles(releasingPath, "*.md");
        }
        else
        {
            var unreleasedFragments = Directory.Exists(unreleasedPath)
                ? Directory.GetFiles(unreleasedPath, "*.md")
                : Array.Empty<string>();

            if (unreleasedFragments.Length == 0)
            {
                throw new InvalidOperationException("No unreleased fragments found to release.");
            }

            // Validate all fragments before moving any of them
            var validationParser = new ChangelogParser();
            foreach (var f in unreleasedFragments)
            {
                string content = File.ReadAllText(f, Encoding.UTF8);
                var doc = Markdig.Markdown.Parse(content);
                if (doc.Count == 0 || doc.FirstOrDefault() is not Markdig.Syntax.HeadingBlock hb || hb.Level != 3)
                {
                    throw new InvalidOperationException(
                        $"Invalid fragment detected: '{Path.GetRelativePath(_basePath, f)}'. " +
                        "Fragment must start with a level 3 heading (### Category). " +
                        "Run 'changesharp validate' for details.");
                }
                var changeSet = validationParser.Parse(content);
                if (changeSet.IsEmpty())
                {
                    throw new InvalidOperationException(
                        $"Invalid fragment detected: '{Path.GetRelativePath(_basePath, f)}'. " +
                        "Fragment is empty or has no recognized categories. " +
                        "Run 'changesharp validate' for details.");
                }
            }

            if (!Directory.Exists(releasingPath)) Directory.CreateDirectory(releasingPath);
            foreach (var f in unreleasedFragments)
            {
                File.Move(f, Path.Combine(releasingPath, Path.GetFileName(f)));
            }
            fragments = Directory.GetFiles(releasingPath, "*.md");
        }

        // 2. Compute release data
        var parser = new ChangelogParser();
        var changeSets = fragments.Select(f => parser.Parse(File.ReadAllText(f, Encoding.UTF8))).ToList();
        var mergedChangeSet = changeSets.Aggregate(new ChangeSet(), (a, b) => a.Merge(b));
        
        string currentVersion = GetCurrentVersion(config);
        
        // 3. Update CHANGELOG.md
        string changelogContent = File.Exists(changelogPath) 
            ? File.ReadAllText(changelogPath, Encoding.UTF8) 
            : GetDefaultChangelog();
            
        var changeLog = new ChangeLog(changelogContent);
        string nextVersion;

        // Check if current version already contains these changes (resumed run)
        string? currentContent = changeLog.GetVersionContent(currentVersion);
        if (currentVersion != "0.0.0" && currentContent != null && currentContent == mergedChangeSet.ToChangelogString().Trim())
        {
            nextVersion = forcedVersion ?? currentVersion;
        }
        else
        {
            var (computedVersion, versionWarning) = NextVersionComputer.ComputeVersionWithWarning(currentVersion, mergedChangeSet, config.SemverPolicy);
            nextVersion = forcedVersion ?? computedVersion;
            
            if (versionWarning != null)
                warnings.Add(versionWarning);

            string? existingContent = changeLog.GetVersionContent(nextVersion);
            if (existingContent != null && existingContent == mergedChangeSet.ToChangelogString().Trim())
            {
            }
            else if (existingContent != null)
            {
                throw new InvalidOperationException($"Conflict: Version {nextVersion} already exists in {config.ChangelogPath} but with different content. Manual intervention required.");
            }
            else
            {
                var updatedChangelog = changeLog.ReleaseWithVersion(releaseDate, nextVersion, mergedChangeSet.ToChangelogString());
                File.WriteAllText(changelogPath, updatedChangelog.ToString(), Encoding.UTF8);
            }
        }

        // 4. Propagate version to targets FIRST (before cleanup, so a failure is recoverable)
        warnings.AddRange(PropagateVersion(config, nextVersion));

        // 5. Cleanup processed fragments (only after successful propagation)
        foreach (var file in fragments)
        {
            File.Delete(file);
        }

        return (nextVersion, warnings.ToArray());
    }

    private string GetCurrentVersion(ChangeSharpConfig config)
    {
        string changelogPath = Path.Combine(_basePath, config.ChangelogPath);
        if (File.Exists(changelogPath))
        {
            string changelogContent = File.ReadAllText(changelogPath, Encoding.UTF8);
            var changeLog = new ChangeLog(changelogContent);
            return changeLog.LastVersion;
        }
        return "0.0.0";
    }

    public (string Version, string Body) GetLatestRelease()
    {
        var config = LoadConfig();
        string changelogPath = Path.Combine(_basePath, config.ChangelogPath);
        if (!File.Exists(changelogPath))
        {
            throw new InvalidOperationException($"Changelog not found at '{changelogPath}'.");
        }

        var changeLog = new ChangeLog(File.ReadAllText(changelogPath, Encoding.UTF8));
        string version = changeLog.LastVersion;
        if (version == "0.0.0")
        {
            throw new InvalidOperationException("No released version found in the changelog.");
        }

        return GetVersionRelease(version);
    }

    public (string Version, string Body) GetVersionRelease(string version)
    {
        var config = LoadConfig();
        string changelogPath = Path.Combine(_basePath, config.ChangelogPath);
        if (!File.Exists(changelogPath))
        {
            throw new InvalidOperationException($"Changelog not found at '{changelogPath}'.");
        }

        var changeLog = new ChangeLog(File.ReadAllText(changelogPath, Encoding.UTF8));
        string? body = changeLog.GetVersionContent(version);
        if (body == null)
        {
            throw new InvalidOperationException($"Version '{version}' not found in the changelog.");
        }

        return (version, body);
    }

    private string GetDefaultChangelog()
    {
        return @"# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
";
    }

    public IEnumerable<string> GetEffectiveVersionTargets()
    {
        var config = LoadConfig();
        var handlers = GetHandlers();
        
        foreach (var target in config.VersionTargets)
        {
            var handler = handlers.FirstOrDefault(h => h.CanHandle(target));
            if (handler != null)
            {
                yield return target.Path;
            }
        }
    }

    private string[] PropagateVersion(ChangeSharpConfig config, string nextVersion)
    {
        var warnings = new List<string>();
        var handlers = GetHandlers();

        foreach (var target in config.VersionTargets)
        {
            var handler = handlers.FirstOrDefault(h => h.CanHandle(target));
            if (handler != null)
            {
                string? warning = handler.UpdateVersion(_basePath, target, nextVersion);
                if (warning != null) warnings.Add(warning);
            }
            else
            {
                warnings.Add($"No handler found for version target: {target.Path} (type: {target.Type})");
            }
        }

        return warnings.ToArray();
    }

    private static List<IVersionPropagationHandler> GetHandlers()
    {
        return new List<IVersionPropagationHandler>
        {
            new MSBuildVersionHandler(),
            new JsonVersionHandler(),
            new RegexVersionHandler()
        };
    }

    public (string Version, string? Warning) CreatePrerelease(string? branch = null, string? channel = null, bool dryRun = false)
    {
        var config = LoadConfig();
        string branchName = branch ?? GetCurrentBranch();
        string branchSlug = SanitizeBranchName(branchName);
        
        string identifier = branchSlug;
        if (!string.IsNullOrEmpty(channel))
        {
            identifier = $"{branchSlug}.{channel}";
        }

        GetStatus(out int count, out ChangeSet merged, out string current, out string nextStable);

        if (count == 0)
        {
            throw new InvalidOperationException("No unreleased fragments found to create a pre-release.");
        }

        // Determine next counter
        int counter = 1;
        string? warning = null;
        string prereleaseDir = Path.Combine(_basePath, config.PrereleasesDir, branchSlug);
        string infoPath = Path.Combine(prereleaseDir, "info.json");

        if (File.Exists(infoPath))
        {
            try
            {
                string json = File.ReadAllText(infoPath, Encoding.UTF8);
                var info = JsonSerializer.Deserialize<PrereleaseInfo>(json);
                if (info != null)
                {
                    if (info.BaseVersion == nextStable)
                    {
                        counter = info.Counter + 1;
                    }
                    else
                    {
                        counter = 1;
                    }
                }
            }
            catch (JsonException ex)
            {
                warning = $"Corrupt prerelease info.json ({infoPath}), resetting counter: {ex.Message}";
            }
        }

        string nextPrerelease = NextVersionComputer.ComputePrereleaseVersion(current, merged, identifier, counter, config.SemverPolicy);

        if (dryRun) return (nextPrerelease, warning);

        if (!Directory.Exists(prereleaseDir)) Directory.CreateDirectory(prereleaseDir);
        var newInfo = new PrereleaseInfo
        {
            Version = nextPrerelease,
            BaseVersion = nextStable,
            Counter = counter,
            Branch = branchName,
            Timestamp = DateTime.UtcNow
        };
        string newJson = JsonSerializer.Serialize(newInfo, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(infoPath, newJson, Encoding.UTF8);

        return (nextPrerelease, warning);
    }

    public (List<PrereleaseInfo> Prereleases, string[] Warnings) ListPrereleases()
    {
        var config = LoadConfig();
        string prereleasesPath = Path.Combine(_basePath, config.PrereleasesDir);
        var result = new List<PrereleaseInfo>();
        var warnings = new List<string>();

        if (!Directory.Exists(prereleasesPath)) return (result, []);

        foreach (var dir in Directory.GetDirectories(prereleasesPath))
        {
            string infoPath = Path.Combine(dir, "info.json");
            if (File.Exists(infoPath))
            {
                try
                {
                    string json = File.ReadAllText(infoPath, Encoding.UTF8);
                    var info = JsonSerializer.Deserialize<PrereleaseInfo>(json);
                    if (info != null) result.Add(info);
                }
                catch (JsonException ex)
                {
                    warnings.Add($"Skipping corrupt prerelease info.json ({infoPath}): {ex.Message}");
                }
            }
        }

        return (result.OrderByDescending(x => x.Timestamp).ToList(), warnings.ToArray());
    }

    public string PromotePrerelease(string? branch = null, bool dryRun = false)
    {
        var config = LoadConfig();
        string branchName = branch ?? GetCurrentBranch();
        string branchSlug = SanitizeBranchName(branchName);
        
        string prereleaseDir = Path.Combine(_basePath, config.PrereleasesDir, branchSlug);
        string infoPath = Path.Combine(prereleaseDir, "info.json");

        if (!File.Exists(infoPath))
        {
            throw new InvalidOperationException($"No active pre-release found for branch '{branchName}'.");
        }

        string json = File.ReadAllText(infoPath, Encoding.UTF8);
        var info = JsonSerializer.Deserialize<PrereleaseInfo>(json);
        if (info == null) throw new InvalidOperationException("Failed to load pre-release info.");

        if (dryRun)
        {
            // Preview: show what version would be released based on current fragments
            GetStatus(out int count, out _, out _, out string nextVersion);
            if (count == 0)
                throw new InvalidOperationException("No unreleased fragments found to promote.");
            return nextVersion;
        }

        return FinalizePromotion(info, dryRun);
    }

    private string FinalizePromotion(PrereleaseInfo info, bool dryRun)
    {
        var config = LoadConfig();
        GetStatus(out int fragmentCount, out _, out _, out _);

        if (fragmentCount == 0)
        {
            throw new InvalidOperationException("No unreleased fragments found to promote.");
        }

        var (resultVersion, _) = Release(DateTime.Today, dryRun, forcedVersion: null);

        string branchSlug = SanitizeBranchName(info.Branch);
        string prereleaseDir = Path.Combine(_basePath, config.PrereleasesDir, branchSlug);
        if (Directory.Exists(prereleaseDir)) Directory.Delete(prereleaseDir, true);

        return resultVersion;
    }

    public class PrereleaseInfo
    {
        public string Version { get; set; } = "";
        public string BaseVersion { get; set; } = "";
        public int Counter { get; set; }
        public string Branch { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public ChangeSharpConfig LoadConfig()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return new ChangeSharpConfig();
        }

        string json;
        try
        {
            json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read config file '{ConfigFilePath}': {ex.Message}", ex);
        }

        try
        {
            return JsonSerializer.Deserialize<ChangeSharpConfig>(json)
                ?? throw new InvalidOperationException($"Config file '{ConfigFilePath}' is empty or could not be deserialized.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Config file '{ConfigFilePath}' contains invalid JSON: {ex.Message}", ex);
        }
    }

    public string GetCurrentBranch()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = GitExecutable,
                Arguments = "rev-parse --abbrev-ref HEAD",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _basePath
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return "main";
            
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                return output;
            }
        }
        catch
        {
            // Fallback if git is not available or not a repository
        }

        return "main";
    }

    public string SanitizeBranchName(string branchName, PreReleaseConfig? preReleaseConfig = null)
    {
        preReleaseConfig ??= LoadConfig().PreRelease;
        if (!preReleaseConfig.SanitizeBranchName) return branchName;

        // Replace / and other non-alphanumeric with hyphens
        string sanitized = Regex.Replace(branchName, @"[^a-zA-Z0-9]", "-", RegexOptions.None, RegexTimeout);
        
        // Remove duplicate hyphens
        sanitized = Regex.Replace(sanitized, @"-+", "-", RegexOptions.None, RegexTimeout).Trim('-');

        if (sanitized.Length > preReleaseConfig.MaxIdentifierLength)
        {
            sanitized = sanitized.Substring(0, preReleaseConfig.MaxIdentifierLength).Trim('-');
        }

        return sanitized.ToLowerInvariant();
    }

    private static string Slugify(string text)
    {
        string normalized = text.ToLowerInvariant();
        // Replace non-alphanumeric characters with hyphens
        normalized = Regex.Replace(normalized, @"[^a-z0-9\s-]", "", RegexOptions.None, RegexTimeout);
        // Replace multiple spaces/hyphens with a single hyphen
        normalized = Regex.Replace(normalized, @"[\s-]+", "-", RegexOptions.None, RegexTimeout).Trim('-');
        
        if (normalized.Length > 50)
        {
            normalized = normalized.Substring(0, 50).Trim('-');
        }

        return string.IsNullOrEmpty(normalized) ? "change" : normalized;
    }

    private string NormalizeCategory(string category)
    {
        var config = LoadConfig();
        string cleaned = category.Trim();

        foreach (var mapping in config.SemverPolicy.Mappings)
        {
            if (mapping.Key.Equals(cleaned, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.Key;
            }
        }

        // Special case for "breaking" -> "Breaking Changes"
        if (cleaned.Equals("breaking", StringComparison.OrdinalIgnoreCase) && config.SemverPolicy.Mappings.ContainsKey("Breaking Changes"))
        {
            return "Breaking Changes";
        }

        return cleaned;
    }
}

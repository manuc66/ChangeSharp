namespace ChangeSharp;

public class ChangeSharpConfig
{
    public string ChangelogPath { get; set; } = "CHANGELOG.md";
    public string UnreleasedDir { get; set; } = ".changesharp/unreleased";
    public string ReleasingDir { get; set; } = ".changesharp/releasing";
    public string PrereleasesDir { get; set; } = ".changesharp/prereleases";
    public List<VersionTargetConfig> VersionTargets { get; set; } = new();
    public PreReleaseConfig PreRelease { get; set; } = new();
    public SemverPolicyConfig SemverPolicy { get; set; } = new();
    public FragmentNamingConfig FragmentNaming { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
}

public class SecurityConfig
{
    public bool RequireApproval { get; set; } = false;
    public bool AllowAgentRelease { get; set; } = true;
    public bool DryRunByDefault { get; set; } = false;
}

public class FragmentNamingConfig
{
    public bool IncludeBranchName { get; set; } = true;
}

public class SemverPolicyConfig
{
    public Dictionary<string, string> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Breaking Changes", "Major" },
        { "Removed", "Major" },
        { "Changed", "Minor" },
        { "Added", "Minor" },
        { "Deprecated", "Minor" },
        { "Fixed", "Patch" },
        { "Security", "Patch" }
    };

    public string MaxImpact { get; set; } = "major";

    public Dictionary<string, string> BranchMaxImpact { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class PreReleaseConfig
{
    public bool Enabled { get; set; } = true;
    public bool BranchAsIdentifier { get; set; } = true;
    public bool SanitizeBranchName { get; set; } = true;
    public int MaxIdentifierLength { get; set; } = 30;
}

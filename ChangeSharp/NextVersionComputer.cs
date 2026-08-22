using Semver;

namespace ChangeSharp;

public class NextVersionComputer
{
    public static string GetNextSemanticVersion(string changeset, string currentVersion)
    {
        ChangeSet changeSet = new ChangelogParser().Parse(changeset);

        return ComputeVersion(currentVersion, changeSet);
    }

    public static string ComputeVersion(string currentVersion, ChangeSet changeSet, SemverPolicyConfig? policy = null)
    {
        var (version, warning) = ComputeVersionWithWarning(currentVersion, changeSet, policy);
        if (warning != null)
            throw new InvalidOperationException(warning);
        return version;
    }

    public static (string Version, string? Warning) ComputeVersionWithWarning(string currentVersion, ChangeSet changeSet, SemverPolicyConfig? policy = null)
    {
        policy ??= new SemverPolicyConfig();
        
        string prefix = "";
        string versionString = currentVersion;
        if (currentVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            prefix = currentVersion.Substring(0, 1);
            versionString = currentVersion.Substring(1);
        }

        string? warning = null;
        if (!SemVersion.TryParse(versionString, SemVersionStyles.Strict, out var version))
        {
            warning = $"Current version '{currentVersion}' is not a valid SemVer version. Falling back to 0.0.0.";
            version = new SemVersion(0);
        }

        SemVersion nextVersion;

        int maxImpact = 0;
        
        foreach (var pair in changeSet.Sections)
        {
            if (pair.Value.Count > 0)
            {
                if (policy.Mappings.TryGetValue(pair.Key, out var impact))
                {
                    maxImpact = Math.Max(maxImpact, ParseImpact(impact));
                }
            }
        }

        if (maxImpact == 3)
        {
            if (version.IsPrerelease && version.Minor == 0 && version.Patch == 0)
                nextVersion = new SemVersion(version.Major, 0, 0);
            else
                nextVersion = new SemVersion(version.Major + 1, 0, 0);
        }
        else if (maxImpact == 2)
        {
            if (version.IsPrerelease && version.Patch == 0)
                nextVersion = new SemVersion(version.Major, version.Minor, 0);
            else
                nextVersion = new SemVersion(version.Major, version.Minor + 1, 0);
        }
        else if (maxImpact == 1)
        {
            if (version.IsPrerelease)
                nextVersion = new SemVersion(version.Major, version.Minor, version.Patch);
            else
                nextVersion = new SemVersion(version.Major, version.Minor, version.Patch + 1);
        }
        else
        {
            nextVersion = version;
        }

        return ($"{prefix}{nextVersion}", warning);
    }

    public static int ParseImpact(string impact)
    {
        return impact.ToLowerInvariant() switch
        {
            "major" => 3,
            "minor" => 2,
            "patch" => 1,
            _ => 0
        };
    }

    public static string ImpactName(int impact) => impact switch
    {
        3 => "major",
        2 => "minor",
        1 => "patch",
        _ => "none"
    };

    public static string ComputePrereleaseVersion(string currentVersion, ChangeSet changeSet, string identifier, int counter, SemverPolicyConfig? policy = null)
    {
        string prefix = "";
        string versionString = currentVersion;
        if (currentVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            prefix = currentVersion.Substring(0, 1);
            versionString = currentVersion.Substring(1);
        }

        if (!SemVersion.TryParse(versionString, SemVersionStyles.Strict, out var version))
        {
            throw new InvalidOperationException($"Current version '{currentVersion}' is not a valid SemVer version.");
        }

        // 1. If current version is already a pre-release, we might be continuing it
        // but the spec says "base version continues to be calculated from unreleased changesets"
        // and "final version is promoted without recalculating".
        
        // We need the base version from which we bump
        SemVersion baseVersion = version;
        if (version.IsPrerelease)
        {
            baseVersion = new SemVersion(version.Major, version.Minor, version.Patch);
        }

        // Calculate what the NEXT stable version would be based on fragments
        string baseVersionString = $"{prefix}{baseVersion}";
        string nextStable = ComputeVersion(baseVersionString, changeSet, policy);
        
        // Strip prefix from nextStable for SemVersion parsing
        string nextStableStripped = nextStable;
        if (nextStable.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            nextStableStripped = nextStable.Substring(1);
        
        var nextStableSem = SemVersion.Parse(nextStableStripped, SemVersionStyles.Strict);

        // Result is nextStable + identifier + counter
        return $"{prefix}{nextStableSem}-{identifier}.{counter}";
    }
}
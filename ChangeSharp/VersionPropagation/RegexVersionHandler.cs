using System.Text.RegularExpressions;

namespace ChangeSharp.VersionPropagation;

public class RegexVersionHandler : IVersionPropagationHandler
{
    private readonly TimeSpan _matchTimeout;

    public RegexVersionHandler(TimeSpan? matchTimeout = null)
    {
        _matchTimeout = matchTimeout ?? TimeSpan.FromSeconds(5);
    }

    public bool CanHandle(VersionTargetConfig target)
    {
        return target.Type?.Equals("regex", StringComparison.OrdinalIgnoreCase) == true || 
               !string.IsNullOrEmpty(target.Regex);
    }

    public string? UpdateVersion(string basePath, VersionTargetConfig target, string nextVersion)
    {
        string fullPath = Path.Combine(basePath, target.Path);
        if (!File.Exists(fullPath))
            return $"Regex version target not found: {target.Path}";

        if (string.IsNullOrEmpty(target.Regex))
            return $"Regex target '{target.Path}' has no regex pattern configured.";

        string content = File.ReadAllText(fullPath);

        string updated;
        try
        {
            updated = Regex.Replace(content, target.Regex, m =>
            {
                string replacement = target.Replacement ?? "$VERSION";
                return replacement.Replace("$VERSION", nextVersion);
            }, RegexOptions.None, _matchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return $"Regex target '{target.Path}' timed out.";
        }

        File.WriteAllText(fullPath, updated);
        return null;
    }
}

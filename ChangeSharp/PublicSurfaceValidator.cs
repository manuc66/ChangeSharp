using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChangeSharp;

public static class PublicSurfaceValidator
{
    public static (bool Match, string? Message) BaselinesMatch(
        IReadOnlyDictionary<string, string> generated,
        IReadOnlyDictionary<string, string> committed)
    {
        foreach (var kv in generated)
        {
            if (!committed.TryGetValue(kv.Key, out var committedContent))
                return (false, $"Public-surface baseline '{kv.Key}' is missing. Run scripts/update-public-api.sh and commit it.");

            bool equal = TryParseJson(kv.Value, out var generatedJson) && TryParseJson(committedContent, out var committedJson)
                ? JsonNode.DeepEquals(generatedJson, committedJson)
                : string.Equals(Normalize(kv.Value), Normalize(committedContent), StringComparison.Ordinal);

            if (!equal)
                return (false, $"Public-surface baseline '{kv.Key}' is out of date. Run scripts/update-public-api.sh and commit the result.");
        }

        return (true, null);
    }

    private static bool TryParseJson(string content, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(content);
            return node != null;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    public static string DeriveImpact(
        IReadOnlyDictionary<string, string>? baseSurface,
        IReadOnlyDictionary<string, string> currentSurface)
    {
        if (baseSurface == null)
            return "patch";

        int added = 0;
        int removed = 0;

        foreach (var file in currentSurface)
        {
            var baseLines = baseSurface.TryGetValue(file.Key, out var baseContent)
                ? SplitLines(baseContent)
                : Array.Empty<string>();
            var currentLines = SplitLines(file.Value);
            var currentSet = currentLines.ToHashSet(StringComparer.Ordinal);

            removed += baseLines.Count(l => !currentSet.Contains(l));
            added += currentLines.Count(l => !baseLines.Contains(l));
        }

        foreach (var file in baseSurface)
        {
            if (!currentSurface.ContainsKey(file.Key))
                removed += SplitLines(file.Value).Length;
        }

        return removed > 0 ? "major" : added > 0 ? "minor" : "patch";
    }

    public static string Normalize(string content) =>
        content.Replace("\r\n", "\n").TrimEnd('\n', ' ', '\t');

    private static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
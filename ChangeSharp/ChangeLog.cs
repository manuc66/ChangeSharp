namespace ChangeSharp;

public class ChangeLog
{
    private readonly string _content;
    private string? _lastVersion = null;
    
    public string LastVersion
    {
        get {
            if (_lastVersion != null)
            {
                return _lastVersion;
            } 
            Parse(out List<string> _, out string lastRelease);

            return lastRelease;
        }
    }


    public ChangeLog(string content)
    {
        _content = content;
    }


    public ChangeLog UpdateUnReleased(params string[] changesets)
    {
        string unreleasedChanges = ChangeSetMerger.Merge(changesets).ToChangelogString();

        string[] lines = _content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        List<string> beforeUnreleased = lines.TakeWhile(x => !x.StartsWith("## [Unreleased]")).ToList();

        IEnumerable<string> afterUnreleased = lines.Skip(beforeUnreleased.Count + 1)
            .SkipWhile(x => !x.StartsWith("## "));
        
        string newContent = string.Join(Environment.NewLine,
            beforeUnreleased.Concat(new[]
            {
                "## [Unreleased]" + Environment.NewLine, unreleasedChanges
            }).Concat(afterUnreleased)
        );

        return new ChangeLog(newContent);
    }

    public override string ToString()
    {
        return _content;
    }

    public string? GetVersionContent(string version)
    {
        string[] lines = _content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string headerPrefix = $"## [{version}]";
        
        int startIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(headerPrefix))
            {
                startIndex = i;
                break;
            }
        }
        
        if (startIndex == -1) return null;
        
        var contentLines = new List<string>();
        for (int i = startIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ")) break;
            contentLines.Add(lines[i]);
        }
        
        return string.Join(Environment.NewLine, contentLines).Trim();
    }

    public ChangeLog Release(DateTime dateTime, params string[] changesets )
    {
        return ReleaseWithVersion(dateTime, null, changesets);
    }

    public ChangeLog ReleaseWithVersion(DateTime dateTime, string? forcedVersion, params string[] changesets)
    {
        ChangeSet unreleasedChanges = ChangeSetMerger.Merge(changesets);

        List<string> beforeUnreleased = Parse(out List<string> afterUnreleased, out string lastRelease);

        string newVersion = forcedVersion ?? NextVersionComputer.ComputeVersion(lastRelease, unreleasedChanges);

        string newContent = string.Join(Environment.NewLine,
            beforeUnreleased.Concat(new[]
            {
                "## [Unreleased]",
                "",
                $"## [{newVersion}] - {dateTime:yyyy-MM-dd}{Environment.NewLine}", unreleasedChanges.ToChangelogString()
            }).Concat(afterUnreleased)
        );

        ChangeLog changeLog = new(newContent)
        {
            _lastVersion = newVersion
        };
        return changeLog;
    }

    private List<string> Parse(out List<string> afterUnreleased, out string lastRelease)
    {
        string[] lines = _content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        List<string> beforeUnreleased = lines.TakeWhile(x => !x.StartsWith("## [Unreleased]") && !x.StartsWith("## [")).ToList();

        if (lines.Length > beforeUnreleased.Count && lines[beforeUnreleased.Count].StartsWith("## [Unreleased]"))
        {
            afterUnreleased = lines.Skip(beforeUnreleased.Count + 1)
                .SkipWhile(x => !x.StartsWith("## ")).ToList(); 
        }
        else
        {
            afterUnreleased = lines.Skip(beforeUnreleased.Count).ToList(); 
        }
        
        string? lastReleaseLine = afterUnreleased.Count > 0 ? afterUnreleased[0] : null;

        string? tempLastRelease = null;
        if (lastReleaseLine != null)
        {
            // Extract version from ## [version] - date
            var match = System.Text.RegularExpressions.Regex.Match(
                lastReleaseLine, @"##\s*\[(.*?)\]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));
            if (match.Success)
            {
                tempLastRelease = match.Groups[1].Value;
            }
        }

        tempLastRelease ??= "0.0.0";
        lastRelease = tempLastRelease;

        return beforeUnreleased;
    }
}
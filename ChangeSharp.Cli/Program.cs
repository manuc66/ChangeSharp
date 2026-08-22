using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;

namespace ChangeSharp.Cli;

class Program
{
    internal const int ExitCodeSuccess = 0;
    internal const int ExitCodeGenericError = 1;
    internal const int ExitCodeNoChanges = 2;
    internal const int ExitCodeValidationError = 3;
    internal const int ExitCodeConflict = 4;

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("ChangeSharp - Keep a Changelog. Derive the version.");

        var jsonOption = new Option<bool>("--json") { Description = "Output in JSON format for machine consumption." };

        var initCommand = new Command("init", "Initialize ChangeSharp configuration and directory structure.") { jsonOption };
        initCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            try
            {
                var manager = new WorkspaceManager();
                bool configExists = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "changesharp.json"));
                var targets = manager.Initialize();

                return o.Ok(new
                {
                    action = configExists ? "updated" : "initialized",
                    newTargets = targets.Select(t => new { path = t.Path, type = t.Type }).ToList()
                }, () =>
                {
                    if (configExists)
                    {
                        if (targets.Any())
                        {
                            Console.WriteLine("ChangeSharp workspace updated with new components.");
                            Console.WriteLine("Added version targets:");
                            foreach (var target in targets)
                                Console.WriteLine($"  - {target.Path} ({target.Type})");
                        }
                        else
                        {
                            Console.WriteLine("ChangeSharp workspace is already up to date. No new components discovered.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("ChangeSharp workspace initialized successfully.");
                        if (targets.Any())
                        {
                            Console.WriteLine("Auto-discovered version targets:");
                            foreach (var target in targets)
                                Console.WriteLine($"  - {target.Path} ({target.Type})");
                        }
                        else
                        {
                            Console.WriteLine("No version targets were auto-discovered. You can add them manually to changesharp.json.");
                        }
                    }
                });
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        rootCommand.Add(initCommand);

        var messageArgument = new Argument<string>("message")
        {
            Description = "Description of the changes.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var addedOption      = new Option<bool>("--added")      { Description = "Mark change as Added." };
        var changedOption    = new Option<bool>("--changed")    { Description = "Mark change as Changed." };
        var fixedOption      = new Option<bool>("--fixed")      { Description = "Mark change as Fixed." };
        var removedOption    = new Option<bool>("--removed")    { Description = "Mark change as Removed." };
        var deprecatedOption = new Option<bool>("--deprecated") { Description = "Mark change as Deprecated." };
        var securityOption   = new Option<bool>("--security")   { Description = "Mark change as Security." };
        var breakingOption   = new Option<bool>("--breaking")   { Description = "Mark change as Breaking Changes." };

        var fileOption = new Option<string>("--file") { Description = "Read the change description from a file instead of the message argument, stdin, or a prompt." };
        var allowMajorOption = new Option<bool>("--allow-major") { Description = "Allow a fragment whose impact exceeds SemverPolicy.MaxImpact." };

        var newCommand = new Command("new", "Create a new unreleased changelog fragment.")
        {
            messageArgument, addedOption, changedOption, fixedOption,
            removedOption, deprecatedOption, securityOption, breakingOption,
            fileOption, allowMajorOption, jsonOption,
        };

        newCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            string? message = parseResult.GetValue(messageArgument);

            string? messageFile = parseResult.GetValue(fileOption);
            if (messageFile != null)
            {
                if (!File.Exists(messageFile))
                    return o.Err($"File not found: {messageFile}", ExitCodeGenericError);
                message = File.ReadAllText(messageFile).Trim();
            }
            else if (string.IsNullOrWhiteSpace(message))
            {
                message = Console.IsInputRedirected
                    ? Console.In.ReadToEnd().Trim()
                    : PromptForMessage();
            }

            if (string.IsNullOrWhiteSpace(message))
                return o.Err("Description is required.", ExitCodeValidationError);

            string category;
            bool added = parseResult.GetValue(addedOption);
            bool changed = parseResult.GetValue(changedOption);
            bool fixedOpt = parseResult.GetValue(fixedOption);
            bool removed = parseResult.GetValue(removedOption);
            bool deprecated = parseResult.GetValue(deprecatedOption);
            bool security = parseResult.GetValue(securityOption);
            bool breaking = parseResult.GetValue(breakingOption);
            bool allowMajor = parseResult.GetValue(allowMajorOption);

            bool anyCategoryOptionProvided = added || changed || fixedOpt || removed || deprecated || security || breaking;

            while (true)
            {
                if (anyCategoryOptionProvided)
                {
                    category = breaking ? "Breaking Changes"
                             : removed ? "Removed"
                             : changed ? "Changed"
                             : deprecated ? "Deprecated"
                             : fixedOpt ? "Fixed"
                             : security ? "Security"
                             : "Added";
                }
                else if (Console.IsInputRedirected)
                {
                    return o.Err("Category is required when non-interactive. Use one of --added, --changed, --fixed, --removed, --deprecated, --security, --breaking.", ExitCodeValidationError);
                }
                else
                {
                    category = PromptForCategory(allowMajor);
                }

                try
                {
                    var manager = new WorkspaceManager();
                    string? blockReason = manager.GetCreateFragmentError(category, allowMajor);
                    if (blockReason != null)
                    {
                        if (anyCategoryOptionProvided || Console.IsInputRedirected)
                            return o.Err(blockReason, ExitCodeValidationError);
                        Console.WriteLine();
                        Console.WriteLine($"  {blockReason}");
                        Console.WriteLine("  Choose another category, or rerun with --allow-major.");
                        continue;
                    }
                    string filePath = manager.CreateFragment(message, category);
                    return o.Ok(new
                    {
                        filename = Path.GetFileName(filePath),
                        category,
                        path = filePath
                    }, () => Console.WriteLine($"Created fragment: {Path.GetFileName(filePath)} under category '{category}'"));
                }
                catch (Exception ex) { return o.Err(ex.Message); }
            }
        });
        rootCommand.Add(newCommand);

        var nextOnlyOption = new Option<bool>("--next-only") { Description = "Only output the next version number." };
        var statusCommand = new Command("status", "Show the status of unreleased fragments and computed version bump.")
        {
            nextOnlyOption, jsonOption
        };
        statusCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            bool nextOnly = parseResult.GetValue(nextOnlyOption);
            try
            {
                var manager = new WorkspaceManager();
                manager.GetStatus(out int count, out ChangeSet merged, out string current, out string next);

                if (nextOnly)
                    return o.Ok(new { version = next }, () => Console.WriteLine(next));

                var newTargets = manager.DiscoverNewTargets();

                return o.Ok(new
                {
                    fragmentCount = count,
                    currentVersion = current,
                    nextVersion = next,
                    aggregatedChanges = count > 0 ? merged.ToChangelogString() : null,
                    sections = count > 0 ? merged.Sections.ToDictionary(kv => kv.Key, kv => kv.Value) : null,
                    untrackedTargets = newTargets.Select(t => new { path = t.Path, type = t.Type }).ToList()
                }, () =>
                {
                    Console.WriteLine($"Unreleased fragments found: {count}");
                    if (count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Aggregated Changes:");
                        Console.Write(merged.ToChangelogString());
                        Console.WriteLine();
                        Console.WriteLine($"Computed Version Bump: {current} -> {next}");
                    }

                    if (newTargets.Any())
                    {
                        Console.WriteLine();
                        Console.WriteLine("Warning: New components discovered but not tracked in changesharp.json:");
                        foreach (var target in newTargets)
                            Console.WriteLine($"  - {target.Path} ({target.Type})");
                        Console.WriteLine("Run 'changesharp init' to add them to your configuration.");
                    }
                });
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        rootCommand.Add(statusCommand);

        var requireFragmentsOption = new Option<bool>("--require-fragments") { Description = "Fail if no unreleased fragments are found." };
        var apiMinLevelOption = new Option<string>("--api-min-level") { Description = "Minimum API impact level (patch, minor, major). Fails if fragments are below this level." };
        var apiMinLevelWarnOption = new Option<bool>("--api-min-level-warn") { Description = "Only warn if --api-min-level is not met, do not fail." };
        var validateCommand = new Command("validate", "Validate unreleased fragments for correct format.")
        {
            requireFragmentsOption, apiMinLevelOption, apiMinLevelWarnOption, jsonOption
        };
        validateCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            bool requireFragments = parseResult.GetValue(requireFragmentsOption);
            try
            {
                var manager = new WorkspaceManager();
                var results = manager.Validate();

                if (results.Count == 0)
                {
                    if (requireFragments)
                        return o.Err("No unreleased fragments found, but --require-fragments was specified.", ExitCodeValidationError);
                    return o.Ok(new { fragmentsValidated = 0 }, () => Console.WriteLine("No unreleased fragments found to validate."));
                }

                bool hasErrors = results.Any(r => !r.IsValid);

                if (!hasErrors)
                {
                    string? apiMinLevelValue = parseResult.GetValue(apiMinLevelOption);
                    if (apiMinLevelValue != null)
                    {
                        bool warnOnly = parseResult.GetValue(apiMinLevelWarnOption);
                        var (pass, maxImpact, maxLevelName) = manager.CheckApiMinLevel(apiMinLevelValue);
                        if (!pass)
                        {
                            string message = $"API surface requires at least a '{apiMinLevelValue}' bump, but fragments only reach '{maxLevelName}' (level {maxImpact}).";
                            if (warnOnly)
                                o.Warn(message);
                            else
                                return o.Err(message, ExitCodeValidationError);
                        }
                    }
                }

                var jsonResults = results.Select(r => new { file = r.FilePath, valid = r.IsValid, errors = r.Errors }).ToList();

                if (hasErrors)
                {
                    return o.Err("Validation failed.", ExitCodeValidationError, new
                    {
                        fragmentsValidated = results.Count,
                        results = jsonResults
                    }, () =>
                    {
                        foreach (var r in results)
                        {
                            if (r.IsValid)
                                Console.WriteLine($"\u2713 {r.FilePath}: Valid");
                            else
                            {
                                Console.WriteLine($"\u2717 {r.FilePath}: Invalid");
                                foreach (var e in r.Errors)
                                    Console.WriteLine($"  - {e}");
                            }
                        }
                        Console.WriteLine($"\n{results.Count(r => !r.IsValid)} fragment(s) failed validation.");
                    });
                }

                return o.Ok(new
                {
                    fragmentsValidated = results.Count,
                    results = jsonResults
                }, () =>
                {
                    Console.WriteLine("All fragments are valid.");
                });
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        rootCommand.Add(validateCommand);

        var dryRunOption = new Option<bool>("--dry-run") { Description = "Display what would happen without making any changes." };
        var allowEmptyOption = new Option<bool>("--allow-empty") { Description = "Exit with success even if no unreleased fragments are found." };
        var requireApprovalOption = new Option<bool>("--require-approval") { Description = "Require explicit approval (CHANGESHARP_ALLOW_UNSAFE_RELEASE) to proceed." };
        var allowMajorReleaseOption = new Option<bool>("--allow-major") { Description = "Allow a release whose impact exceeds SemverPolicy.MaxImpact." };
        var releaseCommand = new Command("release", "Aggregate fragments, bump version, update CHANGELOG.md, and clean up.")
        {
            dryRunOption, allowEmptyOption, requireApprovalOption,
            apiMinLevelOption, apiMinLevelWarnOption, allowMajorReleaseOption, jsonOption
        };
        releaseCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            bool dryRun = parseResult.GetValue(dryRunOption);
            bool allowEmpty = parseResult.GetValue(allowEmptyOption);
            bool requireApproval = parseResult.GetValue(requireApprovalOption);

            if (!dryRun)
            {
                try
                {
                    var manager = new WorkspaceManager();
                    if (manager.ShouldDryRunByDefault())
                    {
                        dryRun = true;
                        o.Warn("Security.DryRunByDefault is enabled; running in dry-run mode.");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    o.Warn($"Could not read config to check Security.DryRunByDefault: {ex.Message}");
                }
            }

            if (requireApproval && !dryRun)
            {
                string? envAllow = Environment.GetEnvironmentVariable("CHANGESHARP_ALLOW_UNSAFE_RELEASE");
                if (envAllow != "true")
                    return o.Err("Release blocked by --require-approval. Set CHANGESHARP_ALLOW_UNSAFE_RELEASE=true to proceed.", ExitCodeGenericError, new { blockedBy = "approval_gate" });
            }

            try
            {
                var manager = new WorkspaceManager();
                manager.GetStatus(out int count, out ChangeSet merged, out string current, out string next);

                if (count == 0)
                {
                    if (allowEmpty)
                        return o.Ok(new { message = "No unreleased fragments found. --allow-empty specified.", releasedVersion = (string?)null },
                            () => Console.WriteLine("No unreleased fragments found. --allow-empty specified, exiting with success."));
                    return o.Err("No unreleased fragments found. Nothing to release.", ExitCodeNoChanges);
                }

                var targets = manager.GetEffectiveVersionTargets().ToList();

                if (dryRun)
                {
                    return o.Ok(new
                    {
                        dryRun = true,
                        currentVersion = current,
                        nextVersion = next,
                        changes = merged.ToChangelogString(),
                        sections = merged.Sections.ToDictionary(kv => kv.Key, kv => kv.Value),
                        fragmentCount = count,
                        versionTargets = targets
                    }, () =>
                    {
                        Console.WriteLine("[Dry Run] Release would perform the following actions:");
                        Console.WriteLine($"- Update CHANGELOG.md with a new version section: [{next}]");
                        Console.WriteLine($"- Add the following changes to CHANGELOG.md:");
                        Console.WriteLine(merged.ToChangelogString());
                        Console.WriteLine($"- Delete {count} fragment(s) from the unreleased directory.");

                        if (targets.Any())
                        {
                            Console.WriteLine($"- Propagate version {next} to the following files:");
                            foreach (var target in targets)
                                Console.WriteLine($"  * {target}");
                        }
                        else
                        {
                            Console.WriteLine("- No version propagation targets configured.");
                        }
                        Console.WriteLine();
                        Console.WriteLine("[Dry Run] No files were actually modified.");
                    });
                }

                string? apiMinLevelValue = parseResult.GetValue(apiMinLevelOption);
                bool warnOnly = parseResult.GetValue(apiMinLevelWarnOption);
                bool allowMajor = parseResult.GetValue(allowMajorReleaseOption);

                var gate = manager.GetReleaseGateResult(apiMinLevelValue, allowMajor);
                if (gate.Blocked)
                {
                    if (gate.CapExceeded || !warnOnly)
                        return o.Err(gate.Message, ExitCodeValidationError);
                    o.Warn(gate.Message);
                }

                var (nextVersion, releaseWarnings) = manager.Release(DateTime.Today, dryRun);
                var allWarnings = releaseWarnings.Append(gate.CapExceeded ? "Major bump explicitly allowed via --allow-major." : null)
                    .Where(w => w != null)
                    .Cast<string>()
                    .ToList();
                foreach (var w in allWarnings)
                    Console.Error.WriteLine($"Warning: {w}");
                return o.Ok(new { releasedVersion = nextVersion, warnings = allWarnings },
                    () => Console.WriteLine($"Release successful! New version: {nextVersion}"));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Conflict"))
            {
                return o.Err(ex.Message, ExitCodeConflict, new { conflict = true });
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        rootCommand.Add(releaseCommand);

        var versionOption = new Option<string>("--version") { Description = "Specific released version to output (default: latest)." };
        var publishCommand = new Command("publish", "Output a released version and its changelog segment (for creating a forge release).")
        {
            versionOption, jsonOption
        };
        publishCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            try
            {
                var manager = new WorkspaceManager();
                string? requestedVersion = parseResult.GetValue(versionOption);
                var (version, body) = requestedVersion != null
                    ? manager.GetVersionRelease(requestedVersion)
                    : manager.GetLatestRelease();
                return o.Ok(new { version, tag = $"v{version}", title = version, body },
                    () =>
                    {
                        Console.WriteLine($"Version: {version}");
                        Console.WriteLine($"Tag: v{version}");
                        Console.WriteLine();
                        Console.WriteLine(body);
                    });
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        rootCommand.Add(publishCommand);

        var branchOption  = new Option<string>("--branch") { Description = "Specific branch name to use for pre-release." };
        var listOption    = new Option<bool>("--list") { Description = "List all active pre-releases." };
        var promoteOption = new Option<bool>("--promote") { Description = "Promote the latest pre-release to a final release." };
        var channelOption = new Option<string>("--channel") { Description = "Optional release channel (e.g. alpha, beta, rc)." };

        var prereleaseCommand = new Command("prerelease", "Handle pre-release versions based on branches.")
        {
            branchOption, listOption, promoteOption, channelOption, dryRunOption, jsonOption
        };

        prereleaseCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            string? branch = parseResult.GetValue(branchOption);
            bool list      = parseResult.GetValue(listOption);
            bool promote   = parseResult.GetValue(promoteOption);
            string? channel = parseResult.GetValue(channelOption);
            bool dryRun    = parseResult.GetValue(dryRunOption);

            try
            {
                var manager = new WorkspaceManager();
                if (list)
                {
                    var (listPrereleases, listWarnings) = manager.ListPrereleases();
                    foreach (var w in listWarnings)
                        Console.Error.WriteLine($"Warning: {w}");
                    return o.Ok(new { prereleases = listPrereleases.Select(p => new { p.Version, p.Branch, p.Timestamp }) },
                        () =>
                        {
                            if (!listPrereleases.Any())
                                Console.WriteLine("No active pre-releases found.");
                            else
                            {
                                Console.WriteLine("Active pre-releases:");
                                foreach (var info in listPrereleases)
                                    Console.WriteLine($"- {info.Version} (Branch: {info.Branch}, Date: {info.Timestamp:yyyy-MM-dd HH:mm:ss})");
                            }
                        });
                }
                else if (promote)
                {
                    string finalVersion = manager.PromotePrerelease(branch, dryRun);
                    return o.Ok(new { action = "promote", version = finalVersion, dryRun },
                        () => Console.WriteLine(dryRun
                            ? $"[Dry Run] Would promote to version: {finalVersion}"
                            : $"Promotion successful! New version: {finalVersion}"));
                }
                else
                {
                    var (prereleaseVersion, prereleaseWarning) = manager.CreatePrerelease(branch, channel, dryRun);
                    if (prereleaseWarning != null)
                        Console.Error.WriteLine($"Warning: {prereleaseWarning}");
                    return o.Ok(new { action = "create", version = prereleaseVersion, dryRun },
                        () => Console.WriteLine(dryRun
                            ? $"[Dry Run] Would create pre-release: {prereleaseVersion}"
                            : $"Pre-release created successfully: {prereleaseVersion}"));
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Conflict"))
            {
                return o.Err(ex.Message, ExitCodeConflict, new { conflict = true });
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        rootCommand.Add(prereleaseCommand);

        var listOption2 = new Option<bool>("--list") { Description = "List all unreleased fragments." };
        var allOption = new Option<bool>("--all") { Description = "Remove all unreleased fragments." };
        var yesOption = new Option<bool>("--yes") { Description = "Skip confirmation for --all." };
        var fragmentArgument = new Argument<string>("fragment")
        {
            Description = "Fragment filename to remove (use --list to see available files).",
            Arity = ArgumentArity.ZeroOrOne
        };
        var removeCommand = new Command("remove", "Remove an unreleased changelog fragment.")
        {
            fragmentArgument, listOption2, allOption, yesOption, jsonOption
        };
        removeCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult, jsonOption);
            bool list = parseResult.GetValue(listOption2);
            bool all = parseResult.GetValue(allOption);
            bool yes = parseResult.GetValue(yesOption);
            string? fragment = parseResult.GetValue(fragmentArgument);

            try
            {
                var manager = new WorkspaceManager();
                var files = manager.ListFragmentFiles();
                var shortNames = files.Select(Path.GetFileName).ToArray();

                if (list)
                {
                    return o.Ok(new { fragments = shortNames }, () =>
                    {
                        if (shortNames.Length == 0)
                            Console.WriteLine("No unreleased fragments found.");
                        else
                        {
                            Console.WriteLine("Unreleased fragments:");
                            foreach (var f in shortNames)
                                Console.WriteLine($"  {f}");
                        }
                    });
                }

                if (all)
                {
                    if (shortNames.Length == 0)
                        return o.Ok(new { removed = 0 }, () => Console.WriteLine("No unreleased fragments found."));

                    if (!yes)
                    {
                        Console.Error.WriteLine($"This will remove {shortNames.Length} fragment(s):");
                        foreach (var f in shortNames)
                            Console.Error.WriteLine($"  {f}");
                        Console.Error.Write("Are you sure? (y/N): ");
                        var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                        if (response != "y" && response != "yes")
                            return o.Ok(new { removed = 0 }, () => Console.WriteLine("Removal cancelled."));
                    }

                    int count = manager.RemoveAllFragments();
                    return o.Ok(new { removed = count },
                        () => Console.WriteLine($"Removed {count} fragment(s)."));
                }

                if (fragment == null)
                {
                    if (shortNames.Length == 0)
                        return o.Ok(new { fragments = Array.Empty<string>() },
                            () => Console.WriteLine("No unreleased fragments found."));

                    return o.Ok(new { fragments = shortNames }, () =>
                    {
                        Console.WriteLine("Usage: changesharp remove <fragment>");
                        Console.WriteLine("       changesharp remove --list");
                        Console.WriteLine("       changesharp remove --all");
                        Console.WriteLine();
                        Console.WriteLine("Available fragments:");
                        foreach (var f in shortNames)
                            Console.WriteLine($"  {f}");
                    });
                }

                string fullPath = files.FirstOrDefault(f =>
                    Path.GetFileName(f).Equals(fragment, StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(fragment, StringComparison.OrdinalIgnoreCase)) ?? "";

                if (string.IsNullOrEmpty(fullPath) || !manager.RemoveFragment(fullPath))
                    return o.Err($"Fragment '{fragment}' not found.", ExitCodeGenericError);

                return o.Ok(new { removed = true, fragment },
                    () => Console.WriteLine($"Removed fragment: {fragment}"));
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        rootCommand.Add(removeCommand);

        ParseResult parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static Output Out(ParseResult pr, Option<bool> jsonOption) =>
        new(pr.GetValue(jsonOption));

    private static string? PromptForMessage()
    {
        Console.Write("Enter a description for the change: ");
        return Console.ReadLine();
    }

    private static string PromptForCategory(bool allowMajor)
    {
        var categories = new (string Name, string Description)[]
        {
            ("Added", "New feature"),
            ("Changed", "Modification of an existing feature (backward-incompatible)"),
            ("Fixed", "Bug fix"),
            ("Removed", "Removal of a feature"),
            ("Deprecated", "Future removal warning"),
            ("Security", "Security improvement"),
            ("Breaking Changes", "Backward-incompatible change")
        };

        SemverPolicyConfig? policy = null;
        try
        {
            policy = new WorkspaceManager().LoadConfig().SemverPolicy;
        }
        catch
        {
            // impact display is best-effort
        }

        int maxAllowed = policy == null ? 3 : NextVersionComputer.ParseImpact(policy.MaxImpact);

        string? impactOf(string name) =>
            policy?.Mappings.TryGetValue(name, out var v) == true ? v : null;

        bool isBlocked(string name) =>
            !allowMajor && impactOf(name) is { } impact && NextVersionComputer.ParseImpact(impact) > maxAllowed;

        int selected = 0;
        Console.WriteLine("Select a category (↑/↓ to navigate, Enter to confirm, Esc to cancel, 1-7 to jump):");
        while (true)
        {
            RenderCategoryMenu(categories, selected, impactOf, isBlocked);
            var key = Console.ReadKey(true);
            var next = ApplyCategoryKey(key, categories.Length, selected);
            if (next.IsFinal)
            {
                selected = next.Selected;
                break;
            }
            selected = next.Selected;
            Console.CursorTop -= categories.Length;
        }

        return categories[selected].Name;
    }

    private static void RenderCategoryMenu(
        (string Name, string Description)[] categories, int selected,
        Func<string, string?> impactOf, Func<string, bool> isBlocked)
    {
        for (int i = 0; i < categories.Length; i++)
        {
            Console.CursorLeft = 0;
            string impact = impactOf(categories[i].Name) is { } v ? $" ({v})" : "";
            string blocked = isBlocked(categories[i].Name) ? " ⚠ blocked (MaxImpact)" : "";
            if (i == selected)
            {
                Console.Write("> ");
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(categories[i].Name.PadRight(18));
                Console.ResetColor();
                Console.WriteLine($"{impact}{blocked} — {categories[i].Description}");
            }
            else
            {
                Console.WriteLine($"  {categories[i].Name.PadRight(18)}{impact}{blocked} — {categories[i].Description}");
            }
        }
    }

    private static (bool IsFinal, int Selected) ApplyCategoryKey(ConsoleKeyInfo key, int count, int selected)
    {
        if (key.Key == ConsoleKey.UpArrow && selected > 0) return (false, selected - 1);
        if (key.Key == ConsoleKey.DownArrow && selected < count - 1) return (false, selected + 1);
        if (key.Key == ConsoleKey.Escape) return (true, 0);
        if (key.Key >= ConsoleKey.D1 && key.Key <= ConsoleKey.D7) return (true, key.Key - ConsoleKey.D1);
        if (key.Key == ConsoleKey.Enter) return (true, selected);
        return (false, selected);
    }
}

readonly struct Output
{
    private readonly bool _json;

    public Output(bool json) => _json = json;

    public int Ok(object jsonPayload, Action textAction, int exitCode = 0)
    {
        if (_json)
            Console.WriteLine(JsonSerializer.Serialize(new { success = true, data = jsonPayload }));
        else
            textAction();
        return exitCode;
    }

    public int Err(string message, int exitCode = 1, object? extra = null, Action? textAction = null)
    {
        if (_json)
        {
            var dict = new Dictionary<string, object?> { ["success"] = false, ["error"] = message };
            if (extra != null)
            {
                foreach (var prop in extra.GetType().GetProperties())
                    dict[prop.Name] = prop.GetValue(extra);
            }
            Console.WriteLine(JsonSerializer.Serialize(dict));
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
            textAction?.Invoke();
        }
        return exitCode;
    }

    public void Warn(string message)
    {
        if (_json)
            Console.WriteLine(JsonSerializer.Serialize(new { level = "warning", message }));
        else
            Console.WriteLine($"Warning: {message}");
    }
}

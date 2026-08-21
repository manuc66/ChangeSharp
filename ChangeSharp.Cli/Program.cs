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

        var newCommand = new Command("new", "Create a new unreleased changelog fragment.")
        {
            messageArgument, addedOption, changedOption, fixedOption,
            removedOption, deprecatedOption, securityOption, breakingOption,
            fileOption, jsonOption,
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

            bool anyCategoryOptionProvided = added || changed || fixedOpt || removed || deprecated || security || breaking;

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
            else
            {
                category = PromptForCategory();
            }

            try
            {
                var manager = new WorkspaceManager();
                string filePath = manager.CreateFragment(message, category);
                return o.Ok(new
                {
                    filename = Path.GetFileName(filePath),
                    category,
                    path = filePath
                }, () => Console.WriteLine($"Created fragment: {Path.GetFileName(filePath)} under category '{category}'"));
            }
            catch (Exception ex) { return o.Err(ex.Message); }
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
                    int? apiResult = CheckApiMinLevel(parseResult, manager, apiMinLevelOption, apiMinLevelWarnOption, o);
                    if (apiResult.HasValue) return apiResult.Value;
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
        var releaseCommand = new Command("release", "Aggregate fragments, bump version, update CHANGELOG.md, and clean up.")
        {
            dryRunOption, allowEmptyOption, requireApprovalOption,
            apiMinLevelOption, apiMinLevelWarnOption, jsonOption
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

                int? apiResult = CheckApiMinLevel(parseResult, manager, apiMinLevelOption, apiMinLevelWarnOption, o);
                if (apiResult.HasValue) return apiResult.Value;

                var (nextVersion, releaseWarnings) = manager.Release(DateTime.Today, dryRun);
                foreach (var w in releaseWarnings)
                    Console.Error.WriteLine($"Warning: {w}");
                return o.Ok(new { releasedVersion = nextVersion, warnings = releaseWarnings },
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

    private static int? CheckApiMinLevel(ParseResult parseResult, WorkspaceManager manager,
        Option<string> apiMinLevelOption, Option<bool> apiMinLevelWarnOption, Output o)
    {
        string? minLevel = parseResult.GetValue(apiMinLevelOption);
        if (minLevel == null) return null;

        bool warnOnly = parseResult.GetValue(apiMinLevelWarnOption);
        var (pass, maxImpact, maxLevelName) = manager.CheckApiMinLevel(minLevel);

        if (pass) return null;

        string message = $"API surface requires at least a '{minLevel}' bump, but fragments only reach '{maxLevelName}' (level {maxImpact}).";

        if (warnOnly)
        {
            o.Warn(message);
            return null;
        }

        return o.Err(message, ExitCodeValidationError);
    }

    private static string? PromptForMessage()
    {
        Console.Write("Enter a description for the change: ");
        return Console.ReadLine();
    }

    private static string PromptForCategory()
    {
        Console.WriteLine("Select a category for the change:");
        Console.WriteLine("1. Added (New feature)");
        Console.WriteLine("2. Changed (Modification of existing feature)");
        Console.WriteLine("3. Fixed (Bug fix)");
        Console.WriteLine("4. Removed (Removal of a feature)");
        Console.WriteLine("5. Deprecated (Future removal warning)");
        Console.WriteLine("6. Security (Security improvement)");
        Console.WriteLine("7. Breaking Changes (Backward incompatible change)");
        Console.Write("Selection (1-7, default 1): ");

        return Console.ReadLine() switch
        {
            "2" => "Changed",
            "3" => "Fixed",
            "4" => "Removed",
            "5" => "Deprecated",
            "6" => "Security",
            "7" => "Breaking Changes",
            _ => "Added"
        };
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

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json;

namespace ChangeSharp.Cli;

class Program
{
    internal const int ExitCodeSuccess = 0;
    internal const int ExitCodeGenericError = 1;
    internal const int ExitCodeNoChanges = 2;
    internal const int ExitCodeValidationError = 3;
    internal const int ExitCodeConflict = 4;

    private static readonly Option<bool> JsonOption = new("--json") { Description = "Output in JSON format for machine consumption." };
    private static readonly Argument<string> MessageArgument = new("message")
    {
        Description = "Description of the changes.",
        Arity = ArgumentArity.ZeroOrOne
    };
    private static readonly Option<bool> AddedOption = new("--added") { Description = "Mark change as Added." };
    private static readonly Option<bool> ChangedOption = new("--changed") { Description = "Mark change as Changed." };
    private static readonly Option<bool> FixedOption = new("--fixed") { Description = "Mark change as Fixed." };
    private static readonly Option<bool> RemovedOption = new("--removed") { Description = "Mark change as Removed." };
    private static readonly Option<bool> DeprecatedOption = new("--deprecated") { Description = "Mark change as Deprecated." };
    private static readonly Option<bool> SecurityOption = new("--security") { Description = "Mark change as Security." };
    private static readonly Option<bool> BreakingOption = new("--breaking") { Description = "Mark change as Breaking Changes." };
    private static readonly Option<string> FileOption = new("--file") { Description = "Read the change description from a file instead of the message argument, stdin, or a prompt." };
    private static readonly Option<bool> AllowMajorOption = new("--allow-major") { Description = "Allow a fragment whose impact exceeds SemverPolicy.MaxImpact." };
    private static readonly Option<string> ApiMinLevelOption = new("--api-min-level") { Description = "Minimum API impact level (patch, minor, major). Fails if fragments are below this level." };
    private static readonly Option<bool> ApiMinLevelWarnOption = new("--api-min-level-warn") { Description = "Only warn if --api-min-level is not met, do not fail." };
    private static readonly Option<bool> DryRunOption = new("--dry-run") { Description = "Display what would happen without making any changes." };
    private static readonly Option<bool> SeparateOption = new("--separate") { Description = "Create a new fragment file instead of appending to the open changelist." };
    private static readonly Option<string> FragmentTargetOption = new("--fragment") { Description = "Append to a specific fragment file in the unreleased directory." };
    private static readonly Option<string> ChangelistNameOption = new("--changelist") { Description = "Append to (or create) a deterministically named changelist file." };

    static async Task<int> Main(string[] args)
    {
        ParseResult parseResult = CreateRootCommand().Parse(args);
        return await parseResult.InvokeAsync();
    }

    static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("ChangeSharp - Keep a Changelog. Derive the version.");
        rootCommand.Add(BuildInitCommand());
        rootCommand.Add(BuildNewCommand());
        rootCommand.Add(BuildAddCommand());
        rootCommand.Add(BuildStatusCommand());
        rootCommand.Add(BuildValidateCommand());
        rootCommand.Add(BuildReleaseCommand());
        rootCommand.Add(BuildPublishCommand());
        rootCommand.Add(BuildPrereleaseCommand());
        rootCommand.Add(BuildRemoveCommand());
        return rootCommand;
    }

    private static Command BuildInitCommand()
    {
        var initCommand = new Command("init", "Initialize ChangeSharp configuration and directory structure.") { JsonOption };
        initCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult);
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
        return initCommand;
    }

    private static Command BuildNewCommand()
    {
        var newCommand = new Command("new", "Create a new unreleased changelog fragment.")
        {
            MessageArgument, AddedOption, ChangedOption, FixedOption,
            RemovedOption, DeprecatedOption, SecurityOption, BreakingOption,
            FileOption, AllowMajorOption, JsonOption,
        };
        newCommand.SetAction(parseResult => RunFragmentAction(parseResult, Out(parseResult), forceSeparate: true));
        return newCommand;
    }

    private static Command BuildAddCommand()
    {
        var addCommand = new Command("add", "Add a change to the open changelist, or create a new fragment file.")
        {
            MessageArgument, AddedOption, ChangedOption, FixedOption,
            RemovedOption, DeprecatedOption, SecurityOption, BreakingOption,
            FileOption, AllowMajorOption, SeparateOption, FragmentTargetOption, ChangelistNameOption, JsonOption,
        };
        addCommand.SetAction(parseResult => RunFragmentAction(parseResult, Out(parseResult), forceSeparate: false));
        return addCommand;
    }

    private static int RunFragmentAction(ParseResult parseResult, Output o, bool forceSeparate)
    {
        var (message, messageError) = ResolveFragmentMessage(parseResult, o);
        if (messageError.HasValue) return messageError.Value;

        string? mappedCategory = CategoryFromFlags(
            parseResult.GetValue(BreakingOption), parseResult.GetValue(RemovedOption),
            parseResult.GetValue(ChangedOption), parseResult.GetValue(DeprecatedOption),
            parseResult.GetValue(FixedOption), parseResult.GetValue(SecurityOption));

        bool anyFlagCategory = parseResult.GetValue(AddedOption) || mappedCategory != null;
        bool allowMajor = parseResult.GetValue(AllowMajorOption);
        bool separate = forceSeparate || parseResult.GetValue(SeparateOption);
        string? fragmentTarget = forceSeparate ? null : parseResult.GetValue(FragmentTargetOption);
        string? changelistName = forceSeparate ? null : parseResult.GetValue(ChangelistNameOption);

        while (true)
        {
            string category = ChooseCategory(parseResult, o, allowMajor, anyFlagCategory, mappedCategory, out int? categoryError);
            if (categoryError.HasValue) return categoryError.Value;

            int result = TryAppendFragment(o, message, category, allowMajor, anyFlagCategory, separate, fragmentTarget, changelistName);
            if (result != RetryInteractive)
                return result;
        }
    }

    private const int RetryInteractive = -1;

    private static (string? Message, int? Error) ResolveFragmentMessage(ParseResult parseResult, Output o)
    {
        string? message = parseResult.GetValue(MessageArgument);
        string? messageFile = parseResult.GetValue(FileOption);

        if (messageFile != null)
        {
            if (!File.Exists(messageFile))
                return (null, o.Err($"File not found: {messageFile}", ExitCodeGenericError));
            return (File.ReadAllText(messageFile).Trim(), null);
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = Console.IsInputRedirected
                ? Console.In.ReadToEnd().Trim()
                : PromptForMessage();
        }

        if (string.IsNullOrWhiteSpace(message))
            return (null, o.Err("Description is required.", ExitCodeValidationError));

        return (message, null);
    }

    private static string ChooseCategory(ParseResult parseResult, Output o, bool allowMajor, bool anyFlagCategory, string? mappedCategory, out int? error)
    {
        error = null;

        if (anyFlagCategory)
            return mappedCategory ?? "Added";

        if (Console.IsInputRedirected)
        {
            error = o.Err("Category is required when non-interactive. Use one of --added, --changed, --fixed, --removed, --deprecated, --security, --breaking.", ExitCodeValidationError);
            return "";
        }

        return PromptForCategory(allowMajor);
    }

    private static int TryAppendFragment(Output o, string message, string category, bool allowMajor, bool anyFlagCategory, bool separate, string? fragmentTarget, string? changelistName)
    {
        try
        {
            var manager = new WorkspaceManager();
            string? blockReason = manager.GetCreateFragmentError(category, allowMajor);
            if (blockReason != null)
            {
                if (anyFlagCategory || Console.IsInputRedirected)
                    return o.Err(blockReason, ExitCodeValidationError);
                Console.WriteLine();
                Console.WriteLine($"  {blockReason}");
                Console.WriteLine("  Choose another category, or rerun with --allow-major.");
                return RetryInteractive;
            }

            var (fragmentPath, appended, formattedCategory) = manager.AppendFragment(message, category, separate, fragmentTarget, changelistName);
            return o.Ok(new
            {
                filename = Path.GetFileName(fragmentPath),
                category = formattedCategory,
                appended,
                path = fragmentPath
            }, () => Console.WriteLine(appended
                ? $"Added to {Path.GetFileName(fragmentPath)} under '{formattedCategory}'"
                : $"Created fragment: {Path.GetFileName(fragmentPath)} under category '{formattedCategory}'"));
        }
        catch (Exception ex) { return o.Err(ex.Message); }
    }

    private static string? CategoryFromFlags(bool breaking, bool removed, bool changed, bool deprecated, bool fixedOpt, bool security) =>
        breaking ? "Breaking Changes"
        : removed ? "Removed"
        : changed ? "Changed"
        : deprecated ? "Deprecated"
        : fixedOpt ? "Fixed"
        : security ? "Security"
        : null;

    private static Command BuildStatusCommand()
    {
        var nextOnlyOption = new Option<bool>("--next-only") { Description = "Only output the next version number." };
        var statusCommand = new Command("status", "Show the status of unreleased fragments and computed version bump.")
        {
            nextOnlyOption, JsonOption
        };
        statusCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult);
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
        return statusCommand;
    }

    private static Command BuildValidateCommand()
    {
        var requireFragmentsOption = new Option<bool>("--require-fragments") { Description = "Fail if no unreleased fragments are found." };
        var apiSurfaceOption = new Option<bool>("--api-surface") { Description = "Validate committed public-surface baselines and gate fragments against the derived impact." };
        var validateCommand = new Command("validate", "Validate unreleased fragments for correct format.")
        {
            requireFragmentsOption, ApiMinLevelOption, ApiMinLevelWarnOption, apiSurfaceOption, JsonOption
        };
        validateCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult);
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
                    if (parseResult.GetValue(apiSurfaceOption))
                    {
                        int surfaceResult = CheckApiSurface(parseResult, manager, o);
                        if (surfaceResult != 0) return surfaceResult;
                    }
                    else
                    {
                        int? gateResult = CheckApiMinLevelGate(parseResult, manager, o);
                        if (gateResult.HasValue) return gateResult.Value;
                    }
                }

                var jsonResults = results.Select(r => new { file = r.FilePath, valid = r.IsValid, errors = r.Errors }).ToList();

                return hasErrors
                    ? ReportValidationErrors(o, results, jsonResults)
                    : ReportValidationSuccess(o, results, jsonResults);
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        return validateCommand;
    }

    private static int ReportValidationErrors(Output o, List<ValidationResult> results, object jsonResults) =>
        o.Err("Validation failed.", ExitCodeValidationError, new
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

    private static int ReportValidationSuccess(Output o, List<ValidationResult> results, object jsonResults) =>
        o.Ok(new
        {
            fragmentsValidated = results.Count,
            results = jsonResults
        }, () => Console.WriteLine("All fragments are valid."));

    private static Command BuildReleaseCommand()
    {
        var allowEmptyOption = new Option<bool>("--allow-empty") { Description = "Exit with success even if no unreleased fragments are found." };
        var requireApprovalOption = new Option<bool>("--require-approval") { Description = "Require explicit approval (CHANGESHARP_ALLOW_UNSAFE_RELEASE) to proceed." };
        var allowMajorReleaseOption = new Option<bool>("--allow-major") { Description = "Allow a release whose impact exceeds SemverPolicy.MaxImpact." };
        var releaseCommand = new Command("release", "Aggregate fragments, bump version, update CHANGELOG.md, and clean up.")
        {
            DryRunOption, allowEmptyOption, requireApprovalOption,
            ApiMinLevelOption, ApiMinLevelWarnOption, allowMajorReleaseOption, JsonOption
        };
        releaseCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult);
            bool dryRun = ResolveDryRun(parseResult, o);
            bool allowEmpty = parseResult.GetValue(allowEmptyOption);
            bool requireApproval = parseResult.GetValue(requireApprovalOption);

            int? approvalError = CheckApproval(requireApproval, dryRun, o);
            if (approvalError.HasValue) return approvalError.Value;

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
                    return ShowDryRun(o, current, next, merged, count, targets);
                }

                var gate = manager.GetReleaseGateResult(
                    parseResult.GetValue(ApiMinLevelOption), parseResult.GetValue(allowMajorReleaseOption));

                if (gate.Blocked)
                {
                    bool warnOnly = parseResult.GetValue(ApiMinLevelWarnOption);
                    if (gate.CapExceeded || !warnOnly)
                        return o.Err(gate.Message, ExitCodeValidationError);
                    o.Warn(gate.Message);
                }

                return FinishRelease(manager, o, dryRun, gate);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Conflict"))
            {
                return o.Err(ex.Message, ExitCodeConflict, new { conflict = true });
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        return releaseCommand;
    }

    private static bool ResolveDryRun(ParseResult parseResult, Output o)
    {
        bool dryRun = parseResult.GetValue(DryRunOption);
        if (dryRun) return true;

        try
        {
            var manager = new WorkspaceManager();
            if (manager.ShouldDryRunByDefault())
            {
                o.Warn("Security.DryRunByDefault is enabled; running in dry-run mode.");
                return true;
            }
        }
        catch (InvalidOperationException ex)
        {
            o.Warn($"Could not read config to check Security.DryRunByDefault: {ex.Message}");
        }

        return false;
    }

    private static int? CheckApproval(bool requireApproval, bool dryRun, Output o)
    {
        if (!requireApproval || dryRun) return null;

        string? envAllow = Environment.GetEnvironmentVariable("CHANGESHARP_ALLOW_UNSAFE_RELEASE");
        if (envAllow != "true")
            return o.Err("Release blocked by --require-approval. Set CHANGESHARP_ALLOW_UNSAFE_RELEASE=true to proceed.", ExitCodeGenericError, new { blockedBy = "approval_gate" });
        return null;
    }

    private static int FinishRelease(WorkspaceManager manager, Output o, bool dryRun, (bool Blocked, string Message, bool CapExceeded) gate)
    {
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

    private static Command BuildPublishCommand()
    {
        var versionOption = new Option<string>("--version") { Description = "Specific released version to output (default: latest)." };
        var publishCommand = new Command("publish", "Output a released version and its changelog segment (for creating a forge release).")
        {
            versionOption, JsonOption
        };
        publishCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult);
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
        return publishCommand;
    }

    private static Command BuildPrereleaseCommand()
    {
        var branchOption = new Option<string>("--branch") { Description = "Specific branch name to use for pre-release." };
        var listOption = new Option<bool>("--list") { Description = "List all active pre-releases." };
        var promoteOption = new Option<bool>("--promote") { Description = "Promote the latest pre-release to a final release." };
        var channelOption = new Option<string>("--channel") { Description = "Optional release channel (e.g. alpha, beta, rc)." };

        var prereleaseCommand = new Command("prerelease", "Handle pre-release versions based on branches.")
        {
            branchOption, listOption, promoteOption, channelOption, DryRunOption, JsonOption
        };

        prereleaseCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult);
            string? branch = parseResult.GetValue(branchOption);
            bool list = parseResult.GetValue(listOption);
            bool promote = parseResult.GetValue(promoteOption);
            string? channel = parseResult.GetValue(channelOption);
            bool dryRun = parseResult.GetValue(DryRunOption);

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
        return prereleaseCommand;
    }

    private static Command BuildRemoveCommand()
    {
        var listOption = new Option<bool>("--list") { Description = "List all unreleased fragments." };
        var allOption = new Option<bool>("--all") { Description = "Remove all unreleased fragments." };
        var yesOption = new Option<bool>("--yes") { Description = "Skip confirmation for --all." };
        var fragmentArgument = new Argument<string>("fragment")
        {
            Description = "Fragment filename to remove (use --list to see available files).",
            Arity = ArgumentArity.ZeroOrOne
        };
        var removeCommand = new Command("remove", "Remove an unreleased changelog fragment.")
        {
            fragmentArgument, listOption, allOption, yesOption, JsonOption
        };
        removeCommand.SetAction(parseResult =>
        {
            var o = Out(parseResult);
            bool list = parseResult.GetValue(listOption);
            bool all = parseResult.GetValue(allOption);
            bool yes = parseResult.GetValue(yesOption);
            string? fragment = parseResult.GetValue(fragmentArgument);

            try
            {
                var manager = new WorkspaceManager();
                var shortNames = manager.ListFragmentFiles().Select(Path.GetFileName).ToArray();

                if (list)
                    return ShowFragmentList(o, shortNames);

                if (all)
                    return RemoveAllFragments(manager, o, shortNames, yes);

                if (fragment == null)
                    return ShowRemoveUsage(o, shortNames);

                return RemoveSingleFragment(manager, o, fragment);
            }
            catch (Exception ex) { return o.Err(ex.Message); }
        });
        return removeCommand;
    }

    private static int ShowFragmentList(Output o, string[] shortNames) =>
        o.Ok(new { fragments = shortNames }, () =>
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

    private static int RemoveAllFragments(WorkspaceManager manager, Output o, string[] shortNames, bool yes)
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

    private static int ShowRemoveUsage(Output o, string[] shortNames) =>
        o.Ok(new { fragments = shortNames }, () =>
        {
            if (shortNames.Length == 0)
            {
                Console.WriteLine("No unreleased fragments found.");
                return;
            }

            Console.WriteLine("Usage: changesharp remove <fragment>");
            Console.WriteLine("       changesharp remove --list");
            Console.WriteLine("       changesharp remove --all");
            Console.WriteLine();
            Console.WriteLine("Available fragments:");
            foreach (var f in shortNames)
                Console.WriteLine($"  {f}");
        });

    private static int RemoveSingleFragment(WorkspaceManager manager, Output o, string fragment)
    {
        var files = manager.ListFragmentFiles();
        string fullPath = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals(fragment, StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(fragment, StringComparison.OrdinalIgnoreCase)) ?? "";

        if (string.IsNullOrEmpty(fullPath) || !manager.RemoveFragment(fullPath))
            return o.Err($"Fragment '{fragment}' not found.", ExitCodeGenericError);

        return o.Ok(new { removed = true, fragment },
            () => Console.WriteLine($"Removed fragment: {fragment}"));
    }

    private static Output Out(ParseResult pr) =>
        new(pr.GetValue(JsonOption));

    private static int? CheckApiMinLevelGate(ParseResult parseResult, WorkspaceManager manager, Output o)
    {
        string? apiMinLevelValue = parseResult.GetValue(ApiMinLevelOption);
        if (apiMinLevelValue == null) return null;

        bool warnOnly = parseResult.GetValue(ApiMinLevelWarnOption);
        var (pass, maxImpact, maxLevelName) = manager.CheckApiMinLevel(apiMinLevelValue);
        if (pass) return null;

        string message = $"API surface requires at least a '{apiMinLevelValue}' bump, but fragments only reach '{maxLevelName}' (level {maxImpact}).";
        if (warnOnly)
            o.Warn(message);
        else
            return o.Err(message, ExitCodeValidationError);
        return null;
    }

    private static int ShowDryRun(Output o, string current, string next, ChangeSet merged, int count, IEnumerable<string> targets)
    {
        var targetsList = targets.ToList();
        return o.Ok(new
        {
            dryRun = true,
            currentVersion = current,
            nextVersion = next,
            changes = merged.ToChangelogString(),
            sections = merged.Sections.ToDictionary(kv => kv.Key, kv => kv.Value),
            fragmentCount = count,
            versionTargets = targetsList
        }, () =>
        {
            Console.WriteLine("[Dry Run] Release would perform the following actions:");
            Console.WriteLine($"- Update CHANGELOG.md with a new version section: [{next}]");
            Console.WriteLine("- Add the following changes to CHANGELOG.md:");
            Console.WriteLine(merged.ToChangelogString());
            Console.WriteLine($"- Delete {count} fragment(s) from the unreleased directory.");

            if (targetsList.Any())
            {
                Console.WriteLine($"- Propagate version {next} to the following files:");
                foreach (var target in targetsList)
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

    private static int CheckApiSurface(ParseResult parseResult, WorkspaceManager manager, Output o)
    {
        string baselineDir = Path.Combine(Directory.GetCurrentDirectory(), "tests", "public-api");
        if (!Directory.Exists(baselineDir))
            return o.Err($"No public-surface baselines found in '{baselineDir}'. Run scripts/update-public-api.sh first.", ExitCodeGenericError);

        var generated = GenerateSurfaces();
        var committed = ReadBaselines(baselineDir);

        var (match, message) = PublicSurfaceValidator.BaselinesMatch(generated, committed);
        if (!match)
            return o.Err(message ?? "Public-surface baselines are out of date.", ExitCodeValidationError);

        var baseSurface = ReadBaselinesFromGit("origin/main");
        string level = PublicSurfaceValidator.DeriveImpact(baseSurface, committed);

        if (level != "patch")
        {
            manager.GetStatus(out int count, out _, out _, out _);
            if (count == 0)
                return o.Err($"API surface changed (impact '{level}'), but no fragments are recorded. Add a fragment at or above this level.", ExitCodeValidationError);

            var (pass, maxImpact, maxLevelName) = manager.CheckApiMinLevel(level);
            if (!pass)
                return o.Err($"API surface changed (impact '{level}'), but fragments only reach '{maxLevelName}' (level {maxImpact}).", ExitCodeValidationError);
        }

        return o.Ok(new { impact = level, baselines = generated.Keys.OrderBy(k => k).ToList() },
            () => Console.WriteLine($"Public-surface baselines up to date (impact '{level}')."));
    }

    private static Dictionary<string, string> GenerateSurfaces()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var surfaces = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cli-help.txt"] = GenerateCliHelp(CreateRootCommand()),
                ["mcp-tools.json"] = JsonSerializer.Serialize(McpToolCatalog.Tools, new JsonSerializerOptions { WriteIndented = true }),
                ["public-api.txt"] = GeneratePublicApi()
            };

            return surfaces;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static string GenerateCliHelp(RootCommand root)
    {
        var originalOut = Console.Out;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            using var sw = new StringWriter();
            Console.SetOut(sw);

            foreach (var cmd in new[] { root }.Concat(root.Subcommands))
            {
                if (cmd != root)
                {
                    sw.WriteLine();
                    sw.WriteLine($"##### {cmd.Name} #####");
                    sw.WriteLine();
                }

                string[] args = cmd == root ? new[] { "--help" } : new[] { cmd.Name, "--help" };
                root.Parse(args).InvokeAsync().GetAwaiter().GetResult();
            }

            return sw.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static string GeneratePublicApi()
    {
        string api = PublicApiGenerator.ApiGenerator.GeneratePublicApi(typeof(WorkspaceManager).Assembly);
        return api.Replace("\r\n", "\n").TrimEnd() + "\n";
    }

    private static Dictionary<string, string> ReadBaselines(string dir)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(dir))
            files[Path.GetFileName(file)] = File.ReadAllText(file);
        return files;
    }

    private static Dictionary<string, string>? ReadBaselinesFromGit(string rev)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "cli-help.txt", "mcp-tools.json", "public-api.txt" })
        {
            string? content = TryGitShow($"{rev}:tests/public-api/{name}");
            if (content != null)
                result[name] = content;
        }
        return result.Count > 0 ? result : null;
    }

    private static string? TryGitShow(string spec)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add(spec);
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

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
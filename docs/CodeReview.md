# Code Review

> Snapshot of the codebase as of 2026-06-24. Build: OK (0 errors, 6 warnings). Tests: 60/60 passing. Target framework: net10.0.

This document lists the most important findings, ordered by severity.

---

## 1. Documentation promises features that are not implemented

### a. The "JSON-first" CLI does not exist
`docs/Architecture.md`, `docs/Roadmap.md` and `docs/McpIntegration.md` describe the CLI as "JSON-first output (`--json`) for machine consumption". No `--json` option exists in `ChangeSharp.Cli/Program.cs`; output is text-only. This is a pillar of the architecture (CLI as source of truth for CI/CD + MCP) that is not delivered.

### b. "Approval Gates" / enterprise security is fictional
`docs/features/ApprovalGates.md` documents `--require-approval`, a `Security` config section (`RequireApproval`, `AllowAgentRelease`, `DryRunByDefault`) and the `CHANGESHARP_ALLOW_UNSAFE_RELEASE` environment variable. None of this exists in the code (`ChangeSharpConfig.cs`, `Program.cs`, MCP). The MCP `perform_release` tool runs without any gate when `dryRun` is false. `Roadmap.md` marks step 14 as "Completed" — this is incorrect.

### c. "Syntactic Safety Gate": two contradictory designs
`docs/features/ApiSurfaceGate.md` describes the adopted approach (`--api-min-level`, implemented). However `.junie/plans/implement-syntactic-safety-gate.md` is still `isActive: true` and describes a competing approach (a `SyntacticGate` config that runs an external tool) that is not implemented. The roadmap mixes the two. Needs to be unified and clarified.

---

## 2. Semantic configuration inconsistencies

### a. `Changed`: docs say Major by default, code says Minor, repo overrides to Major
- `ChangeSharp/ChangeSharpConfig.cs` → default `Changed -> Minor` (confirmed by `WorkspaceManagerTests.cs` and `GetNextSemanticVersionTests.cs`).
- `docs/SemVer Rules.md` → "By default, ChangeSharp treats 'Changed' as a Major bump to be conservative." Outdated; contradicts the code.
- `changesharp.json` → the repository overrides itself to `Changed -> Major`.
- `Roadmap.md` says "updated from Major to Minor based on user feedback".

Result: a fresh project running `changesharp init` gets `Changed -> Minor`, but the docs claim the opposite, and the project itself uses Major. Inconsistent and misleading.

### b. `MSBuildVersionHandler` overwrites `VersionPrefix` with the full version
`ChangeSharp/VersionPropagation/MSBuildVersionHandler.cs` sets both `Version` and `VersionPrefix` to `nextVersion`. Semantically, `VersionPrefix` is a base for CI pre-release suffixing (MinVer-style); overwriting it with the final version breaks that mechanism for anyone using it. It should either touch only `Version`, or be configurable.

### c. `JsonVersionHandler.JsonPath` only supports top-level properties
`JsonVersionHandler.cs` does `node[propertyName] = nextVersion`. The name `JsonPath` suggests JSONPath (e.g. `$.meta.version`), but only a direct property name works. Misleading name.

---

## 3. Release flow robustness (the functional core)

### a. "Transactional" idempotence is oversold
`docs/ExitCodes.md` promises: "Check if version propagation happened but CHANGELOG.md was not updated." No such check exists in `WorkspaceManager.Release()`. The order is: update CHANGELOG -> **delete fragments** -> propagate versions. If propagation fails midway, fragments are already gone and the state is **unrecoverable** without manual intervention. Resumption only detects fragments left in `releasing/`, not a partial propagation.

### b. Handlers silently skip missing files
`MSBuildVersionHandler.cs`, `JsonVersionHandler.cs`, `RegexVersionHandler.cs` all do `if (!File.Exists(fullPath)) return;` silently. A misconfigured `VersionTarget` makes the release "succeed" while the version is not propagated — breaking the determinism promise. Should at least warn.

### c. `LoadConfig` swallows all exceptions
`WorkspaceManager.cs`: `catch { return new ChangeSharpConfig(); }`. A corrupt `changesharp.json` produces silent default behavior with incomprehensible symptoms for the user.

### d. `NextVersionComputer.ComputeVersion` swallows non-SemVer versions
If parsing fails, it silently falls back to `0.0.0`. A corrupt current version in `CHANGELOG.md` produces a bump from 0.0.0 with no warning.

### e. `PromotePrerelease`: content may be inconsistent with the version
Promotion forces `info.BaseVersion` (computed from fragments **at prerelease creation time**) but re-reads `unreleased/` and aggregates the **current** content. If fragments were added between prerelease creation and promotion, the CHANGELOG will contain the new content under a version computed from the old content. Version/content mismatch.

### f. `CreateFragment`: collision risk at second resolution
`WorkspaceManager.cs` uses `yyyyMMddHHmmss` (second resolution). Two fragments created in the same second, same branch, same message produce the **same filename and silent overwrite** via `File.WriteAllText`. `FrictionlessWorkflow.md` claims collision avoidance; false in this case. Add milliseconds or a random suffix.

---

## 4. Validator vs parser vs release consistency

### a. The validator is stricter than the parser
`WorkspaceManager.cs` rejects any fragment not starting with `###` (level 3). But `ChangelogParser` accepts any heading level. A `## Added` fragment is **rejected by `validate`** but **accepted by `release`**. Inconsistent.

### b. Unknown category + recognized category = validation passes, leaks into changelog
The validation only flags fragments where **none** of the sections are recognized. A fragment with `### Secirity` (typo, real case in `ChangeLogTests.cs`) plus `### Added` passes validation, but "Secirity" is merged as-is into the final CHANGELOG with no SemVer mapping. The test `ItCanUpdateTheUnreleasedSectionByReplacingExistingContent` contains exactly that typo — symptomatic.

---

## 5. Shipped CI/CD vs samples

### a. CI samples target .NET 8 while the project targets net10.0
`samples/ci/github-bot.yml`, `samples/ci/github-release.yml`, `samples/ci/.gitlab-ci.yml` use `8.0.x` / `dotnet/sdk:8.0`, and install the tool from the global NuGet feed (not from the local pack). The current tool (net10.0) will not run on the 8.0 runtime. The samples are **broken** as-is.

### b. `release.yml` triggers a release on every push to main
`release.yml` used to run on every push to main, releasing automatically on each merge — aggressive, and contradictory with the "human-in-the-loop" philosophy. It is now `workflow_dispatch`-only: a release is created on demand from the current state of `main`.

---

## 6. Dependencies and projects

- `ChangeSharp.Cli/ChangeSharp.Cli.csproj` references `System.CommandLine.NamingConventionBinder`, which is unused (the code uses `SetAction(parseResult)`, not the binder). Dead dependency + a mix of stable (`System.CommandLine` 2.0.9) and beta (`2.0.0-beta5.25306.1`) from the same family.
- `ChangeSharp.Mcp` has no `<PackAsTool>` (cannot be installed via `dotnet tool install`). Consistent with the docs (`dotnet run`), but inconsistent with the CLI project.
- `ChangeSharp.Tests.csproj` defines `<Version>1.0.0</Version>` while `IsPackable=false`. Useless.
- No `README.md` at the repository root. The entry point is `docs/ChangeSharp.md`. GitHub convention not followed.

---

## 7. MCP details

- `ChangeSharp.Mcp/Program.cs` uses `DateTime.UtcNow`; `ChangeSharp.Cli/Program.cs` uses `DateTime.Today`. Inconsistent (around midnight, a different day lands in the CHANGELOG).
- `ChangeSharp.Mcp/Program.cs` prints `changeSet.ToString()` (headers `##`) instead of `ToChangelogString()` (`###`) — less faithful to what will land in the changelog.
- `serverInfo.version` is hardcoded to `"1.0.0"` (not synchronized with the assembly version).
- The MCP surface does not expose `init`, `prerelease`/`promote`, or `--api-min-level`. Smaller surface than the CLI.
- Minimal MCP server: no `ping`, `shutdown`, `resources/*`, or cancellation handling. Functional but not complete.

---

## 8. Minor bugs and refinements

- `ChangeLog.UpdateUnReleased` splits only on `Environment.NewLine`, while `Parse` and `GetVersionContent` handle both `\r\n` and `\n`. Fragile cross-platform. Also, `UpdateUnReleased` is only used by tests (dead code in the real flow).
- `ChangeSetMerger.Merge(IEnumerable<ChangeSet>)` is private; the public `Merge(string[])` is only called by tests (the real flow builds its own parser in `WorkspaceManager.GetStatus`).
- `WorkspaceManager.cs`: the `resumed` variable is assigned but never read (CS0219 warning, confirmed by the build).
- `WorkspaceManager.SanitizeBranchName` calls `LoadConfig()` on every invocation (already loaded by the caller, e.g. `CreatePrerelease`).
- `CreatePrerelease` swallows a corrupt `info.json` -> counter resets to 1 -> risk of re-emitting an already-published prerelease version.
- `RegexVersionHandler.cs` replaces the **whole match** (no `$1` support). The test works around it with lookbehind/lookahead. A trap for users.
- `docs/features/Prereleases.md` proposes two filename formats (`20260623-143501-...` and `20260623143501-...`); the code only produces the second (14 glued digits). `docs/FragmentNaming.md` claims the first (with hyphens in the timestamp) — **incorrect**.
- `docs/features/Prereleases.md` marks `--channel` as "Future Enhancement"; it is in fact implemented (`WorkspaceManager.cs`).

---

## Summary

The functional core holds up: Markdig parsing, SemVer calculation, resumable flow, exit codes, MSBuild/JSON/Regex propagation, dogfooding. The 60 tests cover the main paths well.

**Weakness #1 is documentation debt**: at least 3 features marked "Completed" are not (`--json`, Approval Gates, config-driven Safety Gate), a contradictory semantic doc (`Changed` Major/Minor), broken CI samples (net8 vs net10), and an overly optimistic roadmap. For a tool positioned around "trust" and "enterprise-ready", this is the #1 risk.

**Weakness #2 is release transactional robustness**: propagation after fragment deletion + silent skip of missing files + swallowed corrupt config = unrecoverable states with no warning. Needs hardening before qualifying for production.

Suggested priorities:
1. Align docs/code on `Changed`, `--json`, Approval Gates, and the .NET 10 samples.
2. Warn or throw on missing targets and corrupt config.
3. Revisit the release order (propagate before deleting fragments, or journal a checkpoint).
4. Fix the second-resolution fragment name collision.

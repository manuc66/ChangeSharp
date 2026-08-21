# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.1.0] - 2026-08-21

### Added
- Add a Getting Started guide to the documentation site

## [2.0.2] - 2026-08-21

### Fixed
- Convert Obsidian wikilinks to standard Markdown links in docs so they resolve in MkDocs

## [2.0.1] - 2026-08-21

### Fixed
- Skip the release workflow when there are no unreleased fragments instead of failing on dry-run

## [2.0.0] - 2026-08-21

### Changed
- github-release.yml sample: switch from on-push to manual workflow_dispatch trigger
- Version propagation handlers now return warnings when target files are missing instead of silently skipping
- Remove unused System.CommandLine.NamingConventionBinder dependency
- Add PackAsTool to ChangeSharp.Mcp
- Remove unnecessary Version from test project
- MSBuildVersionHandler no longer overwrites VersionPrefix when Version exists (preserves MinVer compatibility)
- MCP: default AllowAgentRelease to true, document security config in McpIntegration.md
- ChangeSet: mark hardcoded category properties as Obsolete
- ChangeLog: re-add [Unreleased] section after each release
- LoadConfig throws on corrupt JSON instead of returning a silent default config
- VersionTargetConfig: omit null Regex/JsonPath/Replacement in JSON output
- WorkspaceManager: return warnings instead of writing to Console.Error
- ComputeVersionWithWarning warns when current version is not valid SemVer instead of silently falling back to 0.0.0
- docs: align Changed default across all docs (code default is Minor)
- Release order: version propagation now happens before fragment cleanup to make failures recoverable
- Roadmap Step 16 aligned with actual --api-min-level implementation

### Added
- Approval gates: Security config section (RequireApproval, AllowAgentRelease, DryRunByDefault)
- --require-approval flag on release command
- CHANGESHARP_ALLOW_UNSAFE_RELEASE environment variable check
- tests: slug truncation, Deindent common indent, prerelease dry-run
- Repository root README.md
- AGENTS.md with high-level guardrails to prevent docs/code drift and silent error handling
- tests: add coverage for hierarchical JsonPath, null Regex, and headingless parser input
- CLI --json output for all commands (init, new, status, validate, release, prerelease)
- RegexVersionHandler: Replacement field with $VERSION placeholder support
- Publish documentation site via MkDocs Material on GitHub Pages
- MCP perform_release tool enforces security policy from changesharp.json
- changesharp remove command to list, delete individual fragments, or remove all

### Fixed
- CI samples updated from .NET 8 to .NET 10 SDK
- CreateFragment timestamp changed from second to millisecond resolution to prevent filename collisions
- tests: fix test naming typos (ComputeAPath->ComputeAPatch, typo fixes)
- SemVer Rules.md Safety Gates section aligned with actual --api-min-level approach
- ExitCodes.md transactional integrity section aligned with actual resume behavior
- ChangelogParser: fix Deindent falsely treating non-heading hash lines as headings
- Prereleases.md: channel examples now include branch slug (matches actual output)
- Prereleases.md: added --dry-run to CLI commands
- MCP: consistent DateTime.Today, ToChangelogString, dynamic server version
- ChangeLog.UpdateUnReleased: handle both \r\n and \n newlines
- CreatePrerelease: warn on corrupt info.json instead of silent reset
- SanitizeBranchName: avoid redundant LoadConfig call
- CLI: decouple --api-min-level from --dry-run so dry-run previews are not blocked
- NextVersionComputer: throw on invalid SemVer instead of silent fallback to 0.0.0
- Grant contents:write permission to the GitHub Actions release workflow so it can push the release commit
- PromotePrerelease no longer forces an outdated BaseVersion, avoiding version/content mismatch when fragments were added between prerelease creation and promotion
- MSBuildVersionHandler: update both Version and VersionPrefix elements
- JsonVersionHandler: warn when JSON path creates intermediate nodes
- Release: validate all fragments before moving to releasing directory
- JsonVersionHandler now supports nested JSON paths via dot notation (e.g. "meta.version")
- SemVer Rules doc: corrected Changed default from Major to Minor to match code
- FragmentNaming.md and Prereleases.md: filename format aligned with code (yyyyMMddHHmmssfff)
- Prereleases.md: --channel marked as implemented (was "Future Enhancement")
- Validator and parser now agree: only level-3 headings (###) are accepted in fragments
- Validator flags each unrecognized category individually instead of only when all are unknown
- Release: respect forcedVersion parameter on resume path

## [1.0.0] - 2026-06-24

### Changed
- Improved determinism of changesharp.json to reduce merge conflicts

### Added
- Documented frictionless merge workflow
- Added MCP server for AI agent integration

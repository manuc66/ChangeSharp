# Feature: Pre-release Channels and Branch-based Versioning

## Goal
Enable automatic pre-release generation from feature branches while preserving ChangeSharp's changelog-driven versioning model.

A pre-release version should:
* Include the branch name as a SemVer pre-release identifier.
* Automatically increment a pre-release counter.
* Preserve chronological ordering of changesets.
* Derive the base version from the aggregated changesets in the branch.

## Examples
`changesharp prerelease --branch feature/payment-api`

Produces: `1.4.0-feature-payment-api.1`

Subsequent executions:
`1.4.0-feature-payment-api.2`
`1.4.0-feature-payment-api.3`

## Version Derivation Rules
The base version continues to be calculated from the unreleased changesets.

| Detected Changes | Base Version |
| :--- | :--- |
| Fixed / Security only | 1.3.1-feature-x.1 |
| Added / Changed / Deprecated | 1.4.0-feature-x.1 |
| Breaking Changes / Removed | 2.0.0-feature-x.1 |

This ensures that pre-release versions remain fully aligned with the final release version that will eventually be published.

## Chronological Ordering
Changesets use timestamp-based filenames with millisecond precision:
`20260624143005123-add-payment-api.md`.

This allows deterministic ordering by filename:
```csharp
Directory.GetFiles(path)
    .OrderBy(x => x);
```
without relying on filesystem metadata.

## Pre-release Storage
```
.changesharp/
├── unreleased/
├── prereleases/
│   ├── feature-payment-api/
│   ├── feature-auth/
│   └── hotfix-123/
```
Each branch maintains its own pre-release history and counter.

## Configuration
```json
{
  "PreRelease": {
    "Enabled": true,
    "BranchAsIdentifier": true,
    "SanitizeBranchName": true,
    "MaxIdentifierLength": 30
  }
}
```

Examples:
* `feature/payment-api` → `feature-payment-api`
* `feature/JIRA-123/payment-api` → `feature-jira-123-payment-api`

## CLI Commands
Generate a pre-release using the current Git branch:
`changesharp prerelease`

Generate a pre-release for a specific branch:
`changesharp prerelease --branch feature/payment-api`

List all active pre-releases:
`changesharp prerelease --list`

Example output:
* `1.4.0-feature-payment-api.3`
* `1.4.0-feature-auth.2`
* `1.3.2-hotfix-123.1`

Promote the latest pre-release to a final release:
`changesharp prerelease --promote`

Example: `1.4.0-feature-payment-api.3` → `1.4.0`

Preview without making changes:
`changesharp prerelease --dry-run`

The final version is calculated from current fragments (not from the pre-release snapshot).

## Release Channels
Prepend a channel label to the pre-release identifier (combined with the branch slug):
* `changesharp prerelease --channel alpha`
* `changesharp prerelease --channel beta`
* `changesharp prerelease --channel rc`

Examples (on branch `feature/payment-api`):
* `1.4.0-feature-payment-api.alpha.1`
* `1.4.0-feature-payment-api.beta.1`
* `1.4.0-feature-payment-api.rc.1`
